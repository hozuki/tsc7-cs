// ported from: internal/checker/checker.go:23750-24035
// （TupleNormalizer（元组元素规范化）、元组元素计数、数组/元组类型谓词、getTypeFromTypeAliasReference）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `type TupleNormalizer struct` → checker 包里的**另一个**类型 `internal sealed class TupleNormalizer`
//    （不是 `partial class Checker` 的成员）；Go 未导出字段 → `_camelCase`。Go 的包级函数与 `(c *Checker)` 方法
//    按契约一律落进同一个 `public sealed partial class Checker`（前者 `internal static`、后者 `internal` 实例方法）。
// 2) Worker 改名（artifacts/checker-method-collisions.md 规则：Go 未导出者与导出包装器同名时加 `Worker`）：
//    `isArrayType`→`IsArrayTypeWorker`、`getElementTypeOfArrayType`→`GetElementTypeOfArrayTypeWorker`、
//    `isArrayLikeType`→`IsArrayLikeTypeWorker`（exports.go 有导出包装器，Exports.cs 已落地）；
//    `isTupleType`→`IsTupleTypeWorker`（exports.go 的导出 `IsTupleType` 已由 Exports.cs 的
//    `public static bool IsTupleType(Type t) => IsTupleTypeWorker(t)` 占用该名字，故 checker.go 侧必须加后缀；
//    本文件所有静态调用点都用 `IsTupleTypeWorker`，否则会与 Exports.cs 的同签名静态方法 CS0111）。
//    `isGenericTupleType`（本窗口的**包级**函数，checker.go:23892）→ `IsGenericTupleTypeWorker`：
//    Go 实例方法 `(c *Checker) isGenericTupleType`（checker.go:25308，C7 片）保留 `IsGenericTupleType`，
//    两者同签名不能并存（Main 2026-09-28 裁决）。
// 3) Go 的 `n.types`/`n.infos` 是 []T：C# 保持数组（`Type[]`/`TupleElementInfo[]`），因为 `TupleType._elementInfos`
//    与 `CreateTupleTypeEx(Type[], TupleElementInfo[], bool)` 都是数组形状；因此 `append(x, y)` → `Core.Concatenate`、
//    `slices.Delete(s, i, j)` → `Core.Splice(s, i, j-i)`。`TupleElementInfo` 在 Types.Compound.cs 里是 struct（值语义），
//    所以 Go "append 时拷贝一份 info" 的语义天然保持（无需 clone）。
// 4) `for i := range n.lastRequiredIndex`（Go 1.22 range-over-int，上界开区间）→ `for (var i = 0; i < _lastRequiredIndex; i++)`。
// 5) Go 在 else-if 里 shadow 了 `aliasSymbol` → C# 改名 `aliasSymbol2`（同名局部变量不能重复声明，语义不变）。
// 6) `core.Map(typeArguments, c.getTypeFromTypeNode)`（实例方法值）→ lambda `n => GetTypeFromTypeNodeWorker(n)`。
// 7) `everyType(lengthType, func(t *Type) bool …)` 的匿名形参在 Go 里遮蔽外层 `t` → C# lambda 形参改名 `x`。
// 8) `jsnum.Number(index)` → `(Number)index`（JsNum.Number 有 int 隐式转换，仓库先例 C5.Destructuring）；`core.IfElse(…, c.undefinedType, nil)` →
//    `IfElse(…, _undefinedType, (Type?)null)`（显式可空转换，避免 null 字面量被推断成非空 `Type` 而报 CS8625）。
// 9) `t.fixedLength`/`t.readonly`/`t.combinedFlags`/`t.elementInfos`（(*TupleType) 的字段）→ `_fixedLength`/`_readonly`/
//    `_combinedFlags`/`_elementInfos`；`TypeAliasLinks.typeParameters` 是 `Type[]?`，Go 的 nil slice 用 `?? []` 兜底
//    （`len(nil)==0` 与 `len([])==0` 等价）。`Core.Map(null, f)` 返回空数组（仓库 Core 约定）而非 Go 的 nil。
// 10) `c.error(c.currentNode, message)` → `Error(_currentNode!, message)`（错误 API 归 C4 片）。
// 11) `getElementTypes` 里 Go 的 `typeArguments[0:arity]` 是切片视图 → C# `typeArguments[..arity]`（复制前缀；
//     `arity == len` 时直接返回原数组，与 Go 的 no-op 语义一致）。
// 12) `core.X`/`ast.X`/`diagnostics.X` 一律 `using static` + 裸名（`IfElse`/`Map`/`Every`/`Splice`/`Concatenate`/
//     `IsPartOfTypeNode`/`IsTypeReferenceType`/`Type_produces_…`）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

// 消歧（探针实测）：usings 在 file-scoped namespace 之前时，`Core.X` 会先在外层命名空间链里命中
// **命名空间** TypeScript.Core（CS0234）。命名空间声明体内的 using 别名优先于外层命名空间 →
// `Core.Map/OrElse/IfElse/...` 绑到静态类；`using static TypeScript.Core.Core;` 的裸名调用不受影响。
using Core = TypeScript.Core.Core;

public sealed class TupleNormalizer
{
    internal Checker _c = null!;
    internal Type[] _types = [];
    internal TupleElementInfo[] _infos = [];
    internal int _lastRequiredIndex;
    internal int _firstRestIndex;
    internal int _lastOptionalOrRestIndex;

    internal bool Normalize(Checker c, Type[] elementTypes, TupleElementInfo[] elementInfos)
    {
        _c = c;
        _lastRequiredIndex = -1;
        _firstRestIndex = -1;
        _lastOptionalOrRestIndex = -1;
        for (var i = 0; i < elementTypes.Length; i++)
        {
            var t = elementTypes[i];
            var info = elementInfos[i];
            if ((info._flags & ElementFlags.Variadic) != 0)
            {
                if ((t._flags & TypeFlags.Any) != 0)
                {
                    Add(t, new TupleElementInfo { _flags = ElementFlags.Rest, _labeledDeclaration = info._labeledDeclaration });
                }
                else if ((t._flags & TypeFlags.InstantiableNonPrimitive) != 0 || c.IsGenericMappedType(t))
                {
                    // Generic variadic elements stay as they are.
                    Add(t, info);
                }
                else if (Checker.IsTupleTypeWorker(t))
                {
                    var spreadTypes = c.GetElementTypes(t);
                    if (spreadTypes.Length + _types.Length >= 10_000)
                    {
                        var message = IfElse(IsPartOfTypeNode(c._currentNode),
                            Type_produces_a_tuple_type_that_is_too_large_to_represent,
                            Expression_produces_a_tuple_type_that_is_too_large_to_represent);
                        c.Error(c._currentNode!, message);
                        return false;
                    }
                    // Spread variadic elements with tuple types into the resulting tuple.
                    var spreadInfos = t.TargetTupleType()!._elementInfos!;
                    for (var j = 0; j < spreadTypes.Length; j++)
                    {
                        Add(spreadTypes[j], spreadInfos[j]);
                    }
                }
                else
                {
                    // Treat everything else as an array type and create a rest element.
                    Type? s = null;
                    if (c.IsArrayLikeTypeWorker(t))
                    {
                        s = c.GetIndexTypeOfTypeWorker(t, c._numberType);
                    }
                    if (s == null)
                    {
                        s = c._errorType;
                    }
                    Add(s, new TupleElementInfo { _flags = ElementFlags.Rest, _labeledDeclaration = info._labeledDeclaration });
                }
            }
            else
            {
                // Copy other element kinds with no change.
                Add(t, info);
            }
        }
        // Turn optional elements preceding the last required element into required elements
        for (var i = 0; i < _lastRequiredIndex; i++)
        {
            if ((_infos[i]._flags & ElementFlags.Optional) != 0)
            {
                _infos[i]._flags = ElementFlags.Required;
            }
        }
        if (_firstRestIndex >= 0 && _firstRestIndex < _lastOptionalOrRestIndex)
        {
            // Turn elements between first rest and last optional/rest into a single rest element
            var types = new Type[_lastOptionalOrRestIndex - _firstRestIndex + 1];
            for (var i = _firstRestIndex; i <= _lastOptionalOrRestIndex; i++)
            {
                var t = _types[i];
                if ((_infos[i]._flags & ElementFlags.Variadic) != 0)
                {
                    t = c.GetIndexedAccessType(t, c._numberType);
                }
                types[i - _firstRestIndex] = t;
            }
            _types[_firstRestIndex] = c.GetUnionTypeWorker(types);
            _types = Splice(_types, _firstRestIndex + 1, _lastOptionalOrRestIndex - _firstRestIndex);
            _infos = Splice(_infos, _firstRestIndex + 1, _lastOptionalOrRestIndex - _firstRestIndex);
        }
        return true;
    }

    internal void Add(Type t, TupleElementInfo info)
    {
        if ((info._flags & ElementFlags.Required) != 0)
        {
            _lastRequiredIndex = _types.Length;
        }
        if ((info._flags & ElementFlags.Rest) != 0 && _firstRestIndex < 0)
        {
            _firstRestIndex = _types.Length;
        }
        if ((info._flags & (ElementFlags.Optional | ElementFlags.Rest)) != 0)
        {
            _lastOptionalOrRestIndex = _types.Length;
        }
        _types = Concatenate(_types, new[] { _c.AddOptionalityEx(t, true /*isProperty*/, (info._flags & ElementFlags.Optional) != 0) });
        _infos = Concatenate(_infos, new[] { info });
    }
}

public sealed partial class Checker
{
    // Return count of starting consecutive tuple elements of the given kind(s)
    internal static int GetStartElementCount(TupleType t, ElementFlags flags)
    {
        var elementInfos = t._elementInfos!;
        for (var i = 0; i < elementInfos.Length; i++)
        {
            if ((elementInfos[i]._flags & flags) == 0)
            {
                return i;
            }
        }
        return elementInfos.Length;
    }

    // Return count of ending consecutive tuple elements of the given kind(s)
    internal static int GetEndElementCount(TupleType t, ElementFlags flags)
    {
        var elementInfos = t._elementInfos!;
        for (var i = elementInfos.Length; i > 0; i--)
        {
            if ((elementInfos[i - 1]._flags & flags) == 0)
            {
                return elementInfos.Length - i;
            }
        }
        return elementInfos.Length;
    }

    internal static int GetTotalFixedElementCount(TupleType t)
    {
        return t._fixedLength + GetEndElementCount(t, ElementFlags.Fixed);
    }

    internal Type[] GetElementTypes(Type t)
    {
        var typeArguments = GetTypeArgumentsWorker(t);
        var arity = GetTypeReferenceArity(t);
        if (typeArguments.Length == arity)
        {
            return typeArguments;
        }
        return typeArguments[..arity];
    }

    internal int GetTypeReferenceArity(Type t)
    {
        return t.TargetInterfaceType()!.TypeParameters().Length;
    }

    internal bool IsArrayTypeWorker(Type t)
    {
        return (t._objectFlags & ObjectFlags.Reference) != 0 && (t.Target() == _globalArrayType || t.Target() == _globalReadonlyArrayType);
    }

    internal bool IsReadonlyArrayType(Type t)
    {
        return (t._objectFlags & ObjectFlags.Reference) != 0 && t.Target() == _globalReadonlyArrayType;
    }

    internal static bool IsTupleTypeWorker(Type t)
    {
        return (t._objectFlags & ObjectFlags.Reference) != 0 && (t.Target()!._objectFlags & ObjectFlags.Tuple) != 0;
    }

    internal static bool IsMutableTupleType(Type t)
    {
        return IsTupleTypeWorker(t) && !t.TargetTupleType()!._readonly;
    }

    internal static bool IsGenericTupleTypeWorker(Type t)
    {
        return IsTupleTypeWorker(t) && (t.TargetTupleType()!._combinedFlags & ElementFlags.Variadic) != 0;
    }

    internal static bool IsSingleElementGenericTupleType(Type t)
    {
        return IsGenericTupleTypeWorker(t) && t.TargetTupleType()!._elementInfos!.Length == 1;
    }

    internal bool IsArrayOrTupleType(Type t)
    {
        return IsArrayTypeWorker(t) || IsTupleTypeWorker(t);
    }

    internal bool IsMutableArrayOrTuple(Type t)
    {
        return IsArrayTypeWorker(t) && !IsReadonlyArrayType(t) || IsTupleTypeWorker(t) && !t.TargetTupleType()!._readonly;
    }

    internal Type? GetElementTypeOfArrayTypeWorker(Type t)
    {
        if (IsArrayTypeWorker(t))
        {
            return GetTypeArgumentsWorker(t)[0];
        }
        return null;
    }

    internal bool IsArrayLikeTypeWorker(Type t)
    {
        // A type is array-like if it is a reference to the global Array or global ReadonlyArray type,
        // or if it is not the undefined or null type and if it is assignable to ReadonlyArray<any>
        return IsArrayTypeWorker(t) || (t._flags & TypeFlags.Nullable) == 0 && IsTypeAssignableToWorker(t, _anyReadonlyArrayType);
    }

    internal bool IsMutableArrayLikeType(Type t)
    {
        // A type is mutable-array-like if it is a reference to the global Array type, or if it is not the
        // any, undefined, null or never type and if it is assignable to Array<any>
        return IsMutableArrayOrTuple(t) || (t._flags & (TypeFlags.Any | TypeFlags.Nullable | TypeFlags.Never)) == 0 && IsTypeAssignableToWorker(t, _anyArrayType);
    }

    internal bool IsEmptyArrayLiteralType(Type t)
    {
        var elementType = GetElementTypeOfArrayTypeWorker(t);
        return elementType != null && IsEmptyLiteralType(elementType);
    }

    internal bool IsEmptyLiteralType(Type t)
    {
        if (_strictNullChecks)
        {
            return t == _implicitNeverType;
        }
        return t == _undefinedWideningType;
    }

    internal bool IsTupleLikeType(Type t)
    {
        if (IsTupleTypeWorker(t) || GetPropertyOfTypeWorker(t, "0") != null)
        {
            return true;
        }
        if (IsArrayLikeTypeWorker(t))
        {
            var lengthType = GetTypeOfPropertyOfTypeWorker(t, "length");
            if (lengthType != null)
            {
                return EveryType(lengthType, x => (x._flags & TypeFlags.NumberLiteral) != 0);
            }
        }
        return false;
    }

    internal bool IsArrayOrTupleLikeType(Type t)
    {
        return IsArrayLikeTypeWorker(t) || IsTupleLikeType(t);
    }

    internal bool IsArrayOrTupleOrIntersection(Type t)
    {
        return (t._flags & TypeFlags.Intersection) != 0 && Every(t.Types(), x => IsArrayOrTupleType(x));
    }

    internal Type? GetTupleElementType(Type t, int index)
    {
        var propType = GetTypeOfPropertyOfTypeWorker(t, index.ToString());
        if (propType != null)
        {
            return propType;
        }
        if (EveryType(t, x => IsTupleTypeWorker(x)))
        {
            return GetTupleElementTypeOutOfStartCount(t, (Number)index, IfElse(_compilerOptions.NoUncheckedIndexedAccess == Tristate.TSTrue, _undefinedType, (Type?)null));
        }
        return null;
    }

    /// <summary>
    /// Get type from reference to type alias. When a type alias is generic, the declared type of the type alias may include
    /// references to the type parameters of the alias. We replace those with the actual type arguments by instantiating the
    /// declared type. Instantiations are cached using the type identities of the type arguments as the key.
    /// </summary>
    internal Type GetTypeFromTypeAliasReference(Node node, Symbol symbol)
    {
        var typeArguments = node.TypeArguments();
        if ((symbol.CheckFlags & CheckFlags.Unresolved) != 0)
        {
            var alias = new TypeAlias { _symbol = symbol, _typeArguments = Map(typeArguments, n => GetTypeFromTypeNodeWorker(n)) };
            var key = GetAliasKey(alias);
            var errorType = _errorTypes.GetValueOrDefault(key);
            if (errorType == null)
            {
                errorType = NewIntrinsicType(TypeFlags.Any, "error");
                errorType._alias = alias;
                _errorTypes[key] = errorType;
            }
            return errorType;
        }
        var t = GetDeclaredTypeOfSymbolWorker(symbol);
        var typeParameters = _typeAliasLinks.Get(symbol)._typeParameters ?? [];
        if (typeParameters.Length != 0)
        {
            var numTypeArguments = typeArguments?.Length ?? 0;
            var minTypeArgumentCount = GetMinTypeArgumentCountWorker(typeParameters);
            if (numTypeArguments < minTypeArgumentCount || numTypeArguments > typeParameters.Length)
            {
                var message = IfElse(minTypeArgumentCount == typeParameters.Length,
                    Generic_type_0_requires_1_type_argument_s,
                    Generic_type_0_requires_between_1_and_2_type_arguments);
                Error(node, message, SymbolToStringWorker(symbol), minTypeArgumentCount, typeParameters.Length);
                return _errorType;
            }
            // We refrain from associating a local type alias with an instantiation of a top-level type alias
            // because the local alias may end up being referenced in an inferred return type where it is not
            // accessible--which in turn may lead to a large structural expansion of the type when generating
            // a .d.ts file. See #43622 for an example.
            var aliasSymbol = GetAliasSymbolForTypeNode(node);
            Symbol? newAliasSymbol = null;
            if (aliasSymbol != null && (IsLocalTypeAlias(symbol) || !IsLocalTypeAlias(aliasSymbol)))
            {
                newAliasSymbol = aliasSymbol;
            }
            Type[]? aliasTypeArguments = null;
            if (newAliasSymbol != null)
            {
                aliasTypeArguments = GetTypeArgumentsForAliasSymbol(newAliasSymbol);
            }
            else if (IsTypeReferenceType(node))
            {
                var aliasSymbol2 = ResolveTypeReferenceName(node, SymbolFlags.Alias, true /*ignoreErrors*/);
                // refers to an alias import/export/reexport - by making sure we use the target as an aliasSymbol,
                // we ensure the exported symbol is used to refer to the type when it is reserialized later
                if (aliasSymbol2 != null && aliasSymbol2 != _unknownSymbol)
                {
                    var resolved = ResolveAliasWorker(aliasSymbol2);
                    if (resolved != null && (resolved.Flags & SymbolFlags.TypeAlias) != 0)
                    {
                        newAliasSymbol = resolved;
                        aliasTypeArguments = GetTypeArgumentsFromNode(node);
                    }
                }
            }
            TypeAlias? newAlias = null;
            if (newAliasSymbol != null)
            {
                newAlias = new TypeAlias { _symbol = newAliasSymbol, _typeArguments = aliasTypeArguments };
            }
            return GetTypeAliasInstantiation(symbol, GetTypeArgumentsFromNode(node), newAlias);
        }
        if (CheckNoTypeArguments(node, symbol))
        {
            return t;
        }
        return _errorType;
    }
}
