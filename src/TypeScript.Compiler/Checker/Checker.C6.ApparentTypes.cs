// ported from: internal/checker/checker.go:22021-22458
// （getTargetSymbol … symbolIsValueEx：apparent type / reduced type 计算、类型参数默认值与类型实参填充、
//   命名成员收集）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 命名/形状按 docs/checker-plan.md §5.5：Go 未导出字段 → `_camelCase`（`t.flags` → `t._flags`、
//    `t.objectFlags` → `t._objectFlags`、`d.resolvedTypeArguments` → `d._resolvedTypeArguments`、
//    `m.resolvedApparentType` → `m._resolvedApparentType`、`c.errorType` → `_errorType`…）；
//    Go 未导出方法 → `internal` + PascalCase；Go 的**包级函数**（无接收者：`isPrototypeProperty`、
//    `isConflictingPrivateProperty`）→ 同一个 `partial class Checker` 的 `internal static` 成员
//    （调用点仍裸名，Parser/Binder/Printer 先例）。
// 2) Worker 改名（artifacts/checker-method-collisions.md，定义点与调用点一致）：
//    `getTargetSymbol`→`GetTargetSymbolWorker`、`getApparentType`→`GetApparentTypeWorker`、
//    `getReducedType`→`GetReducedTypeWorker`、`getTypeArguments`→`GetTypeArgumentsWorker`、
//    `getMinTypeArgumentCount`→`GetMinTypeArgumentCountWorker`、`fillMissingTypeArguments`→`FillMissingTypeArgumentsWorker`、
//    `getDefaultFromTypeParameter`→`GetDefaultFromTypeParameterWorker`；被调用的跨片同名成员同理：
//    `getBaseConstraintOfType`→`GetBaseConstraintOfTypeWorker`、`getTypeFromTypeNode`→`GetTypeFromTypeNodeWorker`、
//    `getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`getUnionType`→`GetUnionTypeWorker`、`symbolToString`→`SymbolToStringWorker`、
//    `typeToStringEx`→`TypeToStringExWorker`。
// 3) `hasCommonDeclaration` 的参数类型写**全限定名** `TypeScript.Collections.OrderedSet<Symbol>`、局部集合写
//    `TypeScript.Collections.Set<Node>`：本命名空间里已有另一个 `OrderedSet<T>`（Go utilities.go 的未导出
//    `orderedSet`，ChkLinksUtils 所有），C# 的名字解析会**静默**优先绑定同命名空间的那个，故此处显式限定。
// 4) Go 的 `collections.Set[*ast.Node]` 零值是可用的空集合 → C# `new TypeScript.Collections.Set<Node>()`。
// 5) Go 的 `slices.Repeat([]*Type{c.errorType}, n)` → `new Type[n]` + `Array.Fill`（Core 没有 Repeat）。
// 6) Go 的 `append(n.OuterTypeParameters(), ...)`（`OuterTypeParameters` 来自 `slices.Clip`，容量==长度，
//    append 必然重新分配）→ `Core.Concatenate`（不是原地追加）。
// 7) Go 的 `for d := range commonDeclarations.Keys()` 里 `Delete` 合法（Go map 允许边遍历边删）；
//    C# 的 `Set<T>.Keys()` 返回**内部 HashSet 本身**，边遍历边改会抛 InvalidOperationException →
//    先 `.ToArray()` 取快照（`HashSet<T>.ToArray()` 是 BCL 实例方法）。
// 8) Go 的 `result[:containedCount]` / `result[containedCount:]` 是与原数组共享底层的切片；C# 的
//    `result[..n]` / `result[n..]` 是**拷贝** → 排序拷贝后 `Array.Copy` 回原数组，等价于原地排序两个区间。
// 9) Go 的 `c.getBaseConstraintOfType(t)` + `if t == nil { t = c.unknownType }` → `?? _unknownType`；
//    `core.IfElse(x != nil, x, y)` → `x ?? y`（`getDefaultOrUnknownFromTypeParameter`）。
// 10) `getApparentType`/`getReducedType` 的 Go `switch { case … }` → if/else if 链（两个 case 的判据互斥，
//     行为一致：Union 与 Intersection 标志不可能同时成立）。
// 11) `getResolvedApparentTypeOfMappedType` 的 Go 闭包 `func(t *Type) bool` 参数名 `t` 会遮蔽外层参数
//     （CS0136）→ 改名 `x`。
// 12) Go 的 `panic("Unhandled case in getTypeArguments")` → `Debug.Fail(...)` + `throw new UnreachableException()`
//     （checker/utilities.go 片先例：`Debug.Fail` 没有 `[DoesNotReturn]`，其后必须补 throw 才能满足返回值控制流）。
// 13) `elaborateNeverIntersection` 的 `chain` 在 Go 里可为 nil（checker.go:19593 传 nil）→ C# `Diagnostic?`
//     参数与 `Diagnostic?` 返回；`node` 为 nil 时 Go 直接透传，故写 `Node?`。
// 14) Go 的 map 读缺失键（`c.cachedTypes[key]` 返回零值）→ `TryGetValue`（C# 索引器读缺失键会抛）。
// 15) Go 的 `getNamedMembers` 在 `members` 为空时返回 nil slice → C# 返回空数组 `[]`：已落地的调用点
//     `Services.cs` 的 `GetAugmentedPropertiesOfType` 把返回值声明成非空 `Symbol[]`，且仓库对 Go nil slice
//     返回值沿用「空数组」约定（先例 `InterfaceType.OuterTypeParameters()` 的 `return [];`）；Go 侧没有任何按
//     nil 判别的调用点（checker.go:25553 无条件赋给 `data.properties`、services.go:293 直接 return）。
// 16) 可空性按**已落地调用点**定（调用点已写死）：`GetTargetSymbolWorker` 入参 `Symbol?`（Exports.cs:77 透传
//     可空入参）、返回非空 `Symbol`（Checker.C2.MemberOverrides.cs:52-53 直接解引用返回值）；
//     `CreateSymbolWithType` 的 `source`/`t` 写 `Symbol?`/`Type?`（Checker.C3.ContextualTypes.cs:155 传
//     `context._thisParameter` 与 `null`＝Go 的 nil type），体内 `source!` 对应 Go 的无条件解引用；
//     `GetMinTypeArgumentCountWorker`/`FillMissingTypeArgumentsWorker`/`GetEffectiveTypeArguments` 的
//     `typeParameters` 写 `Type[]?`（Go 调用点传 `Signature._typeParameters`，其 C# 类型是 `Type[]?`），
//     Go 的 `len(nil) == 0` 语义用 `typeParameters ?? []` 保留。`!`/`??` 只影响静态注解与空判断；
//     运行时行为与 Go 的 nil 完全一致（Go 在这些位置本来就无条件解引用、或按 nil slice 跳过循环）。
//     同理 `InstantiateTypes(typeArguments, d._mapper!)`：被调方（Checker.C6.Instantiation.cs:922）把 mapper
//     声明成非空，而 Go 的 `d.mapper` 可以为 nil（Go 侧由 instantiateType 自己判空），故加 `!` 消注解。
// 17) **using 块放在 file-scoped `namespace TypeScript.Checker;` 之后**（本仓库 Checker 包主流写法：
//     Checker.Keys.cs/Checker.Struct.cs/Utilities.cs/Checker.C5.Members.cs/Checker.C7.TypeNodes.cs/Inference.cs）。
//     理由：`Core`/`Diagnostics`/`Ast`/`Scanner` 这类「既是命名空间、又是同名静态类」的名字，若 using 在
//     namespace **之前**，简单名会先在外层命名空间链里命中**命名空间** `TypeScript.Core` → `Core.Map(...)`
//     报 CS0234（本片最小探针实测；T6Declarations/Main 的 `Diagnostics.X` 同源）。把 using 移进命名空间声明体内，
//     导入的类型成为该命名空间的成员，优先于外层命名空间，`Core.X` 与裸名都可解析。
//     （风格样例 Checker.C6.ReturnTypes.cs 原先 using 在 namespace 之前，现已由 FixUsingLayout 一并移到
//      namespace 之后；本片自始即采用 using 在 namespace 之后的布局。）
// 18) 本文件命名空间体内额外保留了一行 `using Core = TypeScript.Core.Core;`（FixUsingLayout 的全包消歧扫描
//     加进来的）：探针实测「别名写在 file-scoped namespace **之前**」无效（简单名仍先命中外层命名空间 →
//     CS0234），只有写在 namespace **之内**才对 `Core.X` 生效；本片 using 已在 namespace 之后，故该别名是
//     冗余但无害的（与 Checker 包其它 C6 文件保持一致）。

namespace TypeScript.Checker;

// 消歧（探针实测）：usings 在 file-scoped namespace 之前时，`Core.X` 会先在外层命名空间链里命中
// **命名空间** TypeScript.Core（CS0234）。本命名空间作用域内的 using 别名优先于外层命名空间 →
// `Core.Map/OrElse/IfElse/...` 绑到静态类；`using static TypeScript.Core.Core;` 的裸名调用不受影响。
using Core = TypeScript.Core.Core;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    internal Symbol GetTargetSymbolWorker(Symbol? s)
    {
        // if symbol is instantiated its flags are not copied from the 'target'
        // so we'll need to get back original 'target' symbol to work with correct set of flags
        if (s != null && (s.CheckFlags & CheckFlags.Instantiated) != 0)
        {
            return _valueSymbolLinks.Get(s)._target!;
        }
        return s!;
    }

    /// <summary>
    /// Return whether this symbol is a member of a prototype somewhere
    /// Note that this is not tracked well within the compiler, so the answer may be incorrect.
    /// </summary>
    internal static bool IsPrototypeProperty(Symbol symbol)
    {
        return (symbol.Flags & SymbolFlags.Method) != 0 || (symbol.CheckFlags & CheckFlags.SyntheticMethod) != 0;
    }

    internal bool HasCommonDeclaration(TypeScript.Collections.OrderedSet<Symbol> symbols)
    {
        var commonDeclarations = new TypeScript.Collections.Set<Node>();
        foreach (var symbol in symbols.Values())
        {
            var declarations = symbol.Declarations;
            if (declarations == null || declarations.Length == 0)
            {
                return false;
            }
            if (commonDeclarations.Len() == 0)
            {
                foreach (var d in declarations)
                {
                    commonDeclarations.Add(d);
                }
                continue;
            }
            foreach (var d in commonDeclarations.Keys().ToArray())
            {
                if (Array.IndexOf(declarations, d) < 0)
                {
                    commonDeclarations.Delete(d);
                }
            }
            if (commonDeclarations.Len() == 0)
            {
                return false;
            }
        }
        return commonDeclarations.Len() != 0;
    }

    internal Symbol CreateSymbolWithType(Symbol? source, Type? t)
    {
        var symbol = NewSymbolEx(source!.Flags, source.Name, source.CheckFlags & CheckFlags.Readonly);
        symbol.Declarations = source.Declarations;
        symbol.Parent = source.Parent;
        symbol.ValueDeclaration = source.ValueDeclaration;
        var links = _valueSymbolLinks.Get(symbol);
        links._resolvedType = t;
        links._target = source;
        links._nameType = _valueSymbolLinks.Get(source)._nameType;
        return symbol;
    }

    internal bool IsMappedTypeGenericIndexedAccess(Type t)
    {
        if ((t._flags & TypeFlags.IndexedAccess) != 0)
        {
            var indexedAccess = t.AsIndexedAccessType()!;
            var objectType = indexedAccess._objectType!;
            return (objectType._objectFlags & ObjectFlags.Mapped) != 0 && !IsGenericMappedType(objectType) && IsGenericIndexType(indexedAccess._indexType!) &&
                (GetMappedTypeModifiers(objectType) & MappedTypeModifiers.ExcludeOptional) == 0 && objectType.AsMappedType()!._declaration!.NameType == null;
        }
        return false;
    }

    /// <summary>
    /// For a type parameter, return the base constraint of the type parameter. For the string, number,
    /// boolean, and symbol primitive types, return the corresponding object types. Otherwise return the
    /// type itself.
    /// </summary>
    internal Type GetApparentTypeWorker(Type t)
    {
        var originalType = t;
        if ((t._flags & TypeFlags.Instantiable) != 0)
        {
            t = GetBaseConstraintOfTypeWorker(t) ?? _unknownType;
        }
        if ((t._objectFlags & ObjectFlags.Mapped) != 0)
        {
            return GetApparentTypeOfMappedType(t);
        }
        if ((t._objectFlags & ObjectFlags.Reference) != 0 && t != originalType)
        {
            return GetTypeWithThisArgument(t, originalType, false /*needsApparentType*/);
        }
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return GetApparentTypeOfIntersectionType(t, originalType);
        }
        if ((t._flags & TypeFlags.StringLike) != 0)
        {
            return _globalStringType;
        }
        if ((t._flags & TypeFlags.NumberLike) != 0)
        {
            return _globalNumberType;
        }
        if ((t._flags & TypeFlags.BigIntLike) != 0)
        {
            return _getGlobalBigIntType();
        }
        if ((t._flags & TypeFlags.BooleanLike) != 0)
        {
            return _globalBooleanType;
        }
        if ((t._flags & TypeFlags.ESSymbolLike) != 0)
        {
            return _getGlobalESSymbolType();
        }
        if ((t._flags & TypeFlags.NonPrimitive) != 0)
        {
            return _emptyObjectType;
        }
        if ((t._flags & TypeFlags.Index) != 0)
        {
            return _stringNumberSymbolType;
        }
        if ((t._flags & TypeFlags.Unknown) != 0 && !_strictNullChecks)
        {
            return _emptyObjectType;
        }
        return t;
    }

    internal Type GetApparentTypeOfMappedType(Type t)
    {
        var m = t.AsMappedType()!;
        var resolvedApparentType = m._resolvedApparentType;
        if (resolvedApparentType == null)
        {
            resolvedApparentType = GetResolvedApparentTypeOfMappedType(t);
            m._resolvedApparentType = resolvedApparentType;
        }
        return resolvedApparentType;
    }

    internal Type GetResolvedApparentTypeOfMappedType(Type t)
    {
        var mapped = t.AsMappedType()!;
        var target = Core.OrElse(mapped._target, t!);
        var typeVariable = GetHomomorphicTypeVariable(target!);
        if (typeVariable != null && target!.AsMappedType()!._declaration!.NameType == null)
        {
            // We have a homomorphic mapped type or an instantiation of a homomorphic mapped type, i.e. a type
            // of the form { [P in keyof T]: X }. Obtain the modifiers type (the T of the keyof T), and if it is
            // another generic mapped type, recursively obtain its apparent type. Otherwise, obtain its base
            // constraint. Then, if every constituent of the base constraint is an array or tuple type, apply
            // this mapped type to the base constraint. It is safe to recurse when the modifiers type is a
            // mapped type because we protect again circular constraints in getTypeFromMappedTypeNode.
            var modifiersType = GetModifiersTypeFromMappedType(t);
            Type? baseConstraint;
            if (IsGenericMappedType(modifiersType))
            {
                baseConstraint = GetApparentTypeOfMappedType(modifiersType);
            }
            else
            {
                baseConstraint = GetBaseConstraintOfTypeWorker(modifiersType);
            }
            if (baseConstraint != null && EveryType(baseConstraint, x => IsArrayOrTupleType(x) || IsArrayOrTupleOrIntersection(x)))
            {
                return InstantiateType(target, PrependTypeMapping(typeVariable, baseConstraint, mapped._mapper));
            }
        }
        return t;
    }

    internal Type GetApparentTypeOfIntersectionType(Type t, Type thisArgument)
    {
        if (t == thisArgument)
        {
            var d = t.AsIntersectionType()!;
            var resolvedApparentType = d._resolvedApparentType;
            if (resolvedApparentType == null)
            {
                resolvedApparentType = GetTypeWithThisArgument(t, thisArgument, true /*needApparentType*/);
                d._resolvedApparentType = resolvedApparentType;
            }
            return resolvedApparentType;
        }
        var key = new CachedTypeKey(CachedTypeKind.ApparentType, thisArgument._id);
        if (!_cachedTypes.TryGetValue(key, out var result))
        {
            result = GetTypeWithThisArgument(t, thisArgument, true /*needApparentType*/);
            _cachedTypes[key] = result;
        }
        return result!;
    }

    /// <summary>
    /// Return the reduced form of the given type. For a union type, it is a union of the normalized constituent types.
    /// For an intersection of types containing one or more mututally exclusive discriminant properties, it is 'never'.
    /// For all other types, it is simply the type itself. Discriminant properties are considered mutually exclusive when
    /// no constituent property has type 'never', but the intersection of the constituent property types is 'never'.
    /// </summary>
    internal Type GetReducedTypeWorker(Type t)
    {
        if ((t._flags & TypeFlags.Union) != 0)
        {
            if ((t._objectFlags & ObjectFlags.ContainsIntersections) != 0)
            {
                var union = t.AsUnionType()!;
                var cachedReducedType = union._resolvedReducedType;
                if (cachedReducedType != null)
                {
                    return cachedReducedType;
                }
                var reducedType = GetReducedUnionType(t);
                union._resolvedReducedType = reducedType;
                return reducedType;
            }
        }
        else if ((t._flags & TypeFlags.Intersection) != 0)
        {
            if ((t._objectFlags & ObjectFlags.IsNeverIntersectionComputed) == 0)
            {
                t._objectFlags |= ObjectFlags.IsNeverIntersectionComputed;
                if (Core.Some(GetPropertiesOfUnionOrIntersectionType(t), x => IsNeverReducedProperty(x)))
                {
                    t._objectFlags |= ObjectFlags.IsNeverIntersection;
                }
            }
            if ((t._objectFlags & ObjectFlags.IsNeverIntersection) != 0)
            {
                return _neverType;
            }
        }
        return t;
    }

    internal Type GetReducedUnionType(Type unionType)
    {
        var union = unionType.AsUnionType()!;
        var reducedTypes = Core.SameMap(union.Types(), x => GetReducedTypeWorker(x));
        if (Core.Same(reducedTypes, union.Types()))
        {
            return unionType;
        }
        var reduced = GetUnionTypeWorker(reducedTypes);
        if ((reduced._flags & TypeFlags.Union) != 0)
        {
            reduced.AsUnionType()!._resolvedReducedType = reduced;
        }
        return reduced;
    }

    internal bool IsNeverReducedProperty(Symbol prop)
    {
        return IsDiscriminantWithNeverType(prop) || IsConflictingPrivateProperty(prop);
    }

    internal Type GetReducedApparentType(Type t)
    {
        // Since getApparentType may return a non-reduced union or intersection type, we need to perform
        // type reduction both before and after obtaining the apparent type. For example, given a type parameter
        // 'T extends A | B', the type 'T & X' becomes 'A & X | B & X' after obtaining the apparent type, and
        // that type may need further reduction to remove empty intersections.
        return GetReducedTypeWorker(GetApparentTypeWorker(GetReducedTypeWorker(t)));
    }

    internal Diagnostic? ElaborateNeverIntersection(Diagnostic? chain, Node? node, Type t)
    {
        if ((t._flags & TypeFlags.Intersection) != 0 && (t._objectFlags & ObjectFlags.IsNeverIntersection) != 0)
        {
            Symbol? neverProp = Core.Find(GetPropertiesOfUnionOrIntersectionType(t), x => IsDiscriminantWithNeverType(x));
            if (neverProp != null)
            {
                return NewDiagnosticChainForNode(chain, node, The_intersection_0_was_reduced_to_never_because_property_1_has_conflicting_types_in_some_constituents, TypeToStringExWorker(t, null, TypeFormatFlags.NoTypeReduction, null), SymbolToStringWorker(neverProp));
            }
            Symbol? privateProp = Core.Find(GetPropertiesOfUnionOrIntersectionType(t), x => IsConflictingPrivateProperty(x));
            if (privateProp != null)
            {
                return NewDiagnosticChainForNode(chain, node, The_intersection_0_was_reduced_to_never_because_property_1_exists_in_multiple_constituents_and_is_private_in_some, TypeToStringExWorker(t, null, TypeFormatFlags.NoTypeReduction, null), SymbolToStringWorker(privateProp));
            }
        }
        return chain;
    }

    internal bool IsDiscriminantWithNeverType(Symbol prop)
    {
        // Return true for a synthetic non-optional property with non-uniform types, where at least one is
        // a literal type and none is never, that reduces to never.
        return (prop.Flags & SymbolFlags.Optional) == 0 && (prop.CheckFlags & (CheckFlags.NonUniformAndLiteral | CheckFlags.HasNeverType)) == CheckFlags.NonUniformAndLiteral && (GetTypeOfSymbolWorker(prop)._flags & TypeFlags.Never) != 0;
    }

    internal static bool IsConflictingPrivateProperty(Symbol prop)
    {
        // Return true for a synthetic property with multiple declarations, at least one of which is private.
        return prop.ValueDeclaration == null && (prop.CheckFlags & CheckFlags.ContainsPrivate) != 0;
    }

    internal Type[] GetTypeArgumentsWorker(Type t)
    {
        var d = t.AsTypeReference()!;
        if (d._resolvedTypeArguments == null)
        {
            var n = d._target!.AsInterfaceType()!;
            if (!PushTypeResolution(t, TypeSystemPropertyName.ResolvedTypeArguments))
            {
                var repeat = new Type[n.TypeParameters().Length];
                Array.Fill(repeat, _errorType);
                return repeat;
            }
            Type[]? typeArguments = null;
            var node = d._node;
            if (node != null)
            {
                switch (node.Kind)
                {
                    case Kind.TypeReference:
                        typeArguments = Core.Concatenate(n.OuterTypeParameters(), GetEffectiveTypeArguments(node, n.LocalTypeParameters()));
                        break;
                    case Kind.ArrayType:
                        typeArguments = new[] { GetTypeFromTypeNodeWorker(node.AsArrayTypeNode()!.ElementType!) };
                        break;
                    case Kind.TupleType:
                        typeArguments = Core.Map(node.Elements(), x => GetTypeFromTypeNodeWorker(x));
                        break;
                    default:
                        Debug.Fail("Unhandled case in getTypeArguments");
                        throw new System.Diagnostics.UnreachableException();
                }
            }
            if (PopTypeResolution())
            {
                if (d._resolvedTypeArguments == null)
                {
                    d._resolvedTypeArguments = InstantiateTypes(typeArguments, d._mapper!);
                }
            }
            else
            {
                if (d._resolvedTypeArguments == null)
                {
                    var repeat = new Type[n.TypeParameters().Length];
                    Array.Fill(repeat, _errorType);
                    d._resolvedTypeArguments = repeat;
                }
                var errorNode = Core.IfElse(node != null, node, _currentNode);
                if (d._target!._symbol != null)
                {
                    Error(errorNode!, Type_arguments_for_0_circularly_reference_themselves, SymbolToStringWorker(d._target!._symbol));
                }
                else
                {
                    Error(errorNode!, Tuple_type_arguments_circularly_reference_themselves);
                }
            }
        }
        return d._resolvedTypeArguments!;
    }

    internal Type[] GetEffectiveTypeArguments(Node node, Type[]? typeParameters)
    {
        return FillMissingTypeArgumentsWorker(Core.Map(node.TypeArguments(), x => GetTypeFromTypeNodeWorker(x)), typeParameters, GetMinTypeArgumentCountWorker(typeParameters), IsInJSFile(node))!;
    }

    // Gets the minimum number of type arguments needed to satisfy all non-optional type parameters.
    internal int GetMinTypeArgumentCountWorker(Type[]? typeParameters)
    {
        var parameters = typeParameters ?? [];
        var minTypeArgumentCount = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!HasTypeParameterDefault(parameters[i]))
            {
                minTypeArgumentCount = i + 1;
            }
        }
        return minTypeArgumentCount;
    }

    internal bool HasTypeParameterDefault(Type t)
    {
        var symbol = t._symbol;
        return symbol != null && Core.Some(symbol.Declarations, d => IsTypeParameterDeclaration(d) && d.AsTypeParameterDeclaration()!.DefaultType != null);
    }

    internal Type[]? FillMissingTypeArgumentsWorker(Type[]? typeArguments, Type[]? typeParameters, int minTypeArgumentCount, bool isJavaScriptImplicitAny)
    {
        var parameters = typeParameters ?? [];
        var numTypeParameters = parameters.Length;
        if (numTypeParameters == 0)
        {
            return null;
        }
        var numTypeArguments = typeArguments?.Length ?? 0;
        if (isJavaScriptImplicitAny || numTypeArguments < numTypeParameters)
        {
            var result = new Type[numTypeParameters];
            Array.Copy(typeArguments ?? [], result, Math.Min(numTypeArguments, numTypeParameters));
            // Map invalid forward references in default types to the error type
            for (var i = numTypeArguments; i < numTypeParameters; i++)
            {
                result[i] = _errorType;
            }
            var baseDefaultType = GetDefaultTypeArgumentType(isJavaScriptImplicitAny);
            for (var i = numTypeArguments; i < numTypeParameters; i++)
            {
                var defaultType = GetDefaultFromTypeParameterWorker(parameters[i]);

                if (isJavaScriptImplicitAny && defaultType != null && (IsTypeIdenticalTo(defaultType, _unknownType) || IsTypeIdenticalTo(defaultType, _emptyObjectType)))
                {
                    defaultType = _anyType;
                }

                if (defaultType != null)
                {
                    result[i] = InstantiateType(defaultType, NewTypeMapper(parameters, result));
                }
                else
                {
                    result[i] = baseDefaultType;
                }
            }
            return result;
        }
        return typeArguments;
    }

    internal Type GetDefaultTypeArgumentType(bool isInJavaScriptFile)
    {
        if (isInJavaScriptFile)
        {
            return _anyType;
        }
        return _unknownType;
    }

    /// <summary>
    /// Gets the default type for a type parameter. If the type parameter is the result of an instantiation,
    /// this gets the instantiated default type of its target. If the type parameter has no default type or
    /// the default is circular, `undefined` is returned.
    /// </summary>
    internal Type? GetDefaultFromTypeParameterWorker(Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) == 0)
        {
            return null;
        }
        var defaultType = GetResolvedTypeParameterDefault(t);
        if (defaultType != _noConstraintType && defaultType != _circularConstraintType)
        {
            return defaultType;
        }
        return null;
    }

    internal Type? GetResolvedTypeParameterDefault(Type t)
    {
        var d = t.AsTypeParameter()!;
        var resolvedDefaultType = d._resolvedDefaultType;
        if (resolvedDefaultType == null)
        {
            if (d._target != null)
            {
                var targetDefault = GetResolvedTypeParameterDefault(d._target);
                if (targetDefault != null)
                {
                    d._resolvedDefaultType = InstantiateType(targetDefault, d._mapper);
                }
                else
                {
                    d._resolvedDefaultType = _noConstraintType;
                }
            }
            else
            {
                // To block recursion, set the initial value to the resolvingDefaultType.
                d._resolvedDefaultType = _resolvingDefaultType;
                var defaultType = _noConstraintType;
                var symbol = t._symbol;
                if (symbol != null)
                {
                    var defaultDeclaration = Core.FirstNonNil(symbol.Declarations, decl =>
                    {
                        if (IsTypeParameterDeclaration(decl))
                        {
                            return decl.AsTypeParameterDeclaration()!.DefaultType;
                        }
                        return null;
                    });
                    if (defaultDeclaration != null)
                    {
                        defaultType = GetTypeFromTypeNodeWorker(defaultDeclaration);
                    }
                }
                if (d._resolvedDefaultType == _resolvingDefaultType)
                {
                    // If we have not been called recursively, set the correct default type.
                    d._resolvedDefaultType = defaultType;
                }
            }
        }
        else if (resolvedDefaultType == _resolvingDefaultType)
        {
            // If we are called recursively for this type parameter, mark the default as circular.
            d._resolvedDefaultType = _circularConstraintType;
        }
        return d._resolvedDefaultType;
    }

    internal Type GetDefaultOrUnknownFromTypeParameter(Type t)
    {
        var result = GetDefaultFromTypeParameterWorker(t);
        return result ?? _unknownType;
    }

    internal Symbol[] GetNamedMembers(SymbolTable? members, Symbol? container)
    {
        // Go: `if len(members) == 0 { return nil }` —— ast.SymbolTable 是 map，nil map 的 len 为 0（checker.go:22408）。
        if (members is null || members.Count == 0)
        {
            return [];
        }
        // For classes and interfaces, we store explicitly declared members ahead of inherited members. This ensures we process
        // explicitly declared members first in type relations, which is beneficial because explicitly declared members are more
        // likely to contain discriminating differences. See for example https://github.com/microsoft/TypeScript/tsc/issues/1968.
        var result = new List<Symbol>(members.Count);
        var containedCount = 0;
        if (container != null && (container.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
        {
            foreach (var kv in members)
            {
                if (IsNamedMember(kv.Value, kv.Key) && IsDeclarationContainedBy(kv.Value, container))
                {
                    result.Add(kv.Value);
                }
            }
            containedCount = result.Count;
        }
        foreach (var kv in members)
        {
            if (IsNamedMember(kv.Value, kv.Key) && (container == null || (container.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) == 0 || !IsDeclarationContainedBy(kv.Value, container)))
            {
                result.Add(kv.Value);
            }
        }
        var sorted = result.ToArray();
        // Go 的 slices 是共享底层的切片，这里排序的是两个区间各自的拷贝，排序后写回原位置。
        var head = sorted[..containedCount];
        SortSymbols(head);
        Array.Copy(head, 0, sorted, 0, containedCount);
        var tail = sorted[containedCount..];
        SortSymbols(tail);
        Array.Copy(tail, 0, sorted, containedCount, tail.Length);
        return sorted;
    }

    internal bool IsDeclarationContainedBy(Symbol symbol, Symbol container)
    {
        var declaration = symbol.ValueDeclaration;
        if (declaration != null)
        {
            foreach (var d in container.Declarations ?? [])
            {
                if (declaration.Loc.ContainedBy(d.Loc))
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal bool IsNamedMember(Symbol symbol, string id)
    {
        return !IsReservedMemberName(id) && SymbolIsValue(symbol);
    }

    internal bool SymbolIsValue(Symbol symbol)
    {
        return SymbolIsValueEx(symbol, false /*includeTypeOnlyMembers*/);
    }

    internal bool SymbolIsValueEx(Symbol symbol, bool includeTypeOnlyMembers)
    {
        return (symbol.Flags & SymbolFlags.Value) != 0 || (symbol.Flags & SymbolFlags.Alias) != 0 &&
            (GetSymbolFlagsEx(symbol, !includeTypeOnlyMembers, false /*excludeLocalMeanings*/) & SymbolFlags.Value) != 0;
    }
}
