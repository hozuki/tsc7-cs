// ported from: internal/checker/checker.go:24971-25406（infer/import type 节点 + 全局类型 + 数组/元组构造 + 泛型判定）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (c *Checker) x()` → 实例方法（未导出 → `internal` + PascalCase）；Go 的**包级函数**
//    （`isUnaryTupleTypeNode`）→ 同一个 `partial class Checker` 的 `internal static` 成员。
// 2) 命名：Go 未导出字段 → `_camelCase`（`t.flags` → `t._flags`、`c.emptyObjectType` → `_emptyObjectType`、
//    `d.thisType` → `d._thisType`）；枚举成员名 = Go 常量名去类型前缀；`strconv.Itoa` → `.ToString()`。
// 3) 命中 `artifacts/checker-method-collisions.md` 的**未导出**方法（定义在别片）在调用点加 `Worker` 后缀：
//    `getTypeFromTypeNode`→`GetTypeFromTypeNodeWorker`、`getTypeArguments`→`GetTypeArgumentsWorker`、
//    `getUnionType`→`GetUnionTypeWorker`、`getUnionTypeEx`→`GetUnionTypeExWorker`、`getReducedType`→`GetReducedTypeWorker`、
//    `getConstraintOfTypeParameter`→`GetConstraintOfTypeParameterWorker`、`getMergedSymbol`→`GetMergedSymbolWorker`、
//    `resolveExternalModuleName`→`ResolveExternalModuleNameWorker`、`resolveExternalModuleSymbol`→`ResolveExternalModuleSymbolWorker`、
//    `getSymbolFlags`→`GetSymbolFlagsWorker`、`getTypeOfSymbol`→`GetTypeOfSymbolWorker`、
//    `getFullyQualifiedName`→`GetFullyQualifiedNameWorker`；Go 包级未导出 `isTupleType` → `IsTupleTypeWorker`
//    （`Exports.cs` 的导出包装器 `IsTupleType` 转调它）。
// 4) `core.Map/Some/OrElse/IfElse/CountWhere/Concatenate` → Core 同名静态方法（`using static TypeScript.Core.Core`）。
//    `append(slice, x)` → `Concatenate(slice, new T[] { x })` / `List<T>.Add` + `ToArray()`。
// 5) Go 的无条件表达式多分支 `switch { case …: case …: }`（无 fallthrough）→ 顺序 `if`。
// 6) Go 的 `t.data.(*TupleType)` → C# 的 `t.AsTupleType()!`（虚调用，类型不符返回 null；调用点按 Ast 习惯加 `!`）。
// 7) `c.tupleTypes[key]`（Go map 读缺失键返回 nil）→ `_tupleTypes.GetValueOrDefault(key)`。
// 8) Go 的方法 `isGenericTupleType`（checker.go:25308）与包级同名函数（checker.go:23892，C6 片已落地为
//    `internal static bool IsGenericTupleType`）造成 CS0111 → 按 Main 终局裁决：本实例方法保留 `IsGenericTupleType`，
//    包级那个由 C6 片改名 `IsGenericTupleTypeWorker`（static，本片不定义它）。
// 9) `getTupleElementTypeOutOfStartCount` 里 Go 用 `func(t *Type)` 遮蔽了外层参数 `t`；C# 不允许 lambda 形参遮蔽
//    外层参数（CS0136）→ lambda 形参改名 `tupleType`，语义不变。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal Type GetTypeFromInferTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            links._resolvedType = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node.AsInferTypeNode()!.TypeParameter!)!);
        }
        return links._resolvedType!;
    }

    internal Type GetTypeFromImportTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var n = node.AsImportTypeNode()!;
            if (!IsLiteralImportTypeNode(node))
            {
                Error(n.Argument, Diagnostics.String_literal_expected);
                _symbolNodeLinks.Get(node)._resolvedSymbol = _unknownSymbol;
                links._resolvedType = _errorType;
                return links._resolvedType;
            }
            var targetMeaning = IfElse(n.IsTypeOf, SymbolFlags.Value, SymbolFlags.Type);
            // TODO: Future work: support unions/generics/whatever via a deferred import-type
            var innerModuleSymbol = ResolveExternalModuleNameWorker(node, n.Argument!.AsLiteralTypeNode()!.Literal!, false /*ignoreErrors*/, GetTypeFromImportAttributes(GetImportAttributes(node)));
            if (innerModuleSymbol == null)
            {
                _symbolNodeLinks.Get(node)._resolvedSymbol = _unknownSymbol;
                links._resolvedType = _errorType;
                return links._resolvedType;
            }
            var moduleSymbol = ResolveExternalModuleSymbolWorker(innerModuleSymbol, false /*dontResolveAlias*/);
            if (!NodeIsMissing(n.Qualifier))
            {
                var nameChain = GetIdentifierChain(n.Qualifier!);
                var currentNamespace = moduleSymbol;
                for (var i = 0; i < nameChain.Length; i++)
                {
                    var current = nameChain[i];
                    var meaning = SymbolFlags.Namespace;
                    if (i == nameChain.Length - 1)
                    {
                        meaning = targetMeaning;
                    }
                    // typeof a.b.c is normally resolved using `checkExpression` which in turn defers to `checkQualifiedName`
                    // That, in turn, ultimately uses `getPropertyOfType` on the type of the symbol, which differs slightly from
                    // the `exports` lookup process that only looks up namespace members which is used for most type references
                    var mergedResolvedSymbol = GetMergedSymbolWorker(ResolveSymbol(currentNamespace)!)!;
                    Symbol? symbolFromVariable = null;
                    Symbol? symbolFromModule = null;
                    if (n.IsTypeOf)
                    {
                        symbolFromVariable = GetPropertyOfTypeEx(GetTypeOfSymbolWorker(mergedResolvedSymbol), current.Text(), false /*skipObjectFunctionPropertyAugment*/, true /*includeTypeOnlyMembers*/);
                    }
                    else
                    {
                        symbolFromModule = GetSymbol(GetExportsOfSymbol(mergedResolvedSymbol), current.Text(), meaning);
                        if (symbolFromModule == null)
                        {
                            // a CommonJS module might have typedefs exported alongside an export=
                            // !!!
                            var immediateModuleSymbol = ResolveExternalModuleSymbolWorker(innerModuleSymbol, true /*dontResolveAlias*/);
                            if (immediateModuleSymbol != null && Some(immediateModuleSymbol.Declarations, d => GetAssignmentDeclarationKind(d) == JSDeclarationKind.ModuleExports))
                            {
                                symbolFromModule = GetSymbol(GetExportsOfSymbol(immediateModuleSymbol.Parent!), current.Text(), meaning);
                            }
                        }
                    }
                    var next = OrElse(symbolFromModule, symbolFromVariable);
                    if (next == null)
                    {
                        Error(current, Diagnostics.Namespace_0_has_no_exported_member_1, GetFullyQualifiedNameWorker(currentNamespace!, null), DeclarationNameToString(current));
                        links._resolvedType = _errorType;
                        return links._resolvedType;
                    }
                    _symbolNodeLinks.Get(current)._resolvedSymbol = next;
                    _symbolNodeLinks.Get(current.Parent!)._resolvedSymbol = next;
                    currentNamespace = next;
                }
                links._resolvedType = ResolveImportSymbolType(node, currentNamespace!, targetMeaning);
            }
            else
            {
                if ((GetSymbolFlagsWorker(moduleSymbol!) & targetMeaning) != 0)
                {
                    links._resolvedType = ResolveImportSymbolType(node, moduleSymbol!, targetMeaning);
                }
                else
                {
                    var message = IfElse(targetMeaning == SymbolFlags.Value,
                        Diagnostics.Module_0_does_not_refer_to_a_value_but_is_used_as_a_value_here,
                        Diagnostics.Module_0_does_not_refer_to_a_type_but_is_used_as_a_type_here_Did_you_mean_typeof_import_0);
                    Error(node, message, n.Argument!.AsLiteralTypeNode()!.Literal!.Text());
                    _symbolNodeLinks.Get(node)._resolvedSymbol = _unknownSymbol;
                    links._resolvedType = _errorType;
                }
            }
        }
        return links._resolvedType!;
    }

    internal Node[] GetIdentifierChain(Node node)
    {
        if (IsIdentifier(node))
        {
            return new Node[] { node };
        }
        var qualifiedName = node.AsQualifiedName()!;
        return Concatenate(GetIdentifierChain(qualifiedName.Left!), new Node[] { qualifiedName.Right! });
    }

    internal Type ResolveImportSymbolType(Node node, Symbol symbol, SymbolFlags meaning)
    {
        var resolvedSymbol = ResolveSymbol(symbol);
        _symbolNodeLinks.Get(node)._resolvedSymbol = resolvedSymbol;
        if (meaning == SymbolFlags.Value)
        {
            // intentionally doesn't use resolved symbol so type is cached as expected on the alias
            return GetInstantiationExpressionType(GetTypeOfSymbolWorker(symbol), node);
        }
        // getTypeReferenceType doesn't handle aliases - it must get the resolved symbol
        return GetTypeReferenceType(node, resolvedSymbol!);
    }

    internal Type CreateTypeFromGenericGlobalType(Type genericGlobalType, Type[] typeArguments)
    {
        if (genericGlobalType != _emptyGenericType)
        {
            return CreateTypeReference(genericGlobalType, typeArguments);
        }
        return _emptyObjectType;
    }

    internal Type GetGlobalStrictFunctionType(string name)
    {
        if (_strictBindCallApply)
        {
            return GetGlobalType(name, 0 /*arity*/, true /*reportErrors*/);
        }
        return _globalFunctionType;
    }

    internal Type GetGlobalImportMetaExpressionType()
    {
        if (_deferredGlobalImportMetaExpressionType == null)
        {
            // Create a synthetic type `ImportMetaExpression { meta: MetaProperty }`
            var symbol = NewSymbol(SymbolFlags.None, "ImportMetaExpression");
            var importMetaType = _getGlobalImportMetaType();
            var metaPropertySymbol = NewSymbolEx(SymbolFlags.Property, "meta", CheckFlags.Readonly);
            metaPropertySymbol.Parent = symbol;
            _valueSymbolLinks.Get(metaPropertySymbol)._resolvedType = importMetaType;
            var members = CreateSymbolTable(new[] { metaPropertySymbol });
            symbol.Members = members;
            _deferredGlobalImportMetaExpressionType = NewAnonymousType(symbol, members, null, null, null);
        }
        return _deferredGlobalImportMetaExpressionType!;
    }

    internal Type CreateIterableType(Type iteratedType)
    {
        return CreateTypeFromGenericGlobalType(_getGlobalIterableTypeChecked(), new Type[] { iteratedType, _voidType, _undefinedType });
    }

    internal Type CreateArrayType(Type elementType)
    {
        return CreateArrayTypeEx(elementType, false /*readonly*/);
    }

    internal Type CreateArrayTypeEx(Type elementType, bool @readonly)
    {
        return CreateTypeFromGenericGlobalType(IfElse(@readonly, _globalReadonlyArrayType, _globalArrayType), new Type[] { elementType });
    }

    internal ElementFlags GetTupleElementFlags(Node node)
    {
        switch (node.Kind)
        {
            case Kind.OptionalType:
                return ElementFlags.Optional;
            case Kind.RestType:
                return IfElse(GetArrayElementTypeNode(node.Type()!) != null, ElementFlags.Rest, ElementFlags.Variadic);
            case Kind.NamedTupleMember:
                var named = node.AsNamedTupleMember()!;
                if (named.QuestionToken != null)
                {
                    return ElementFlags.Optional;
                }
                if (named.DotDotDotToken != null)
                {
                    return IfElse(GetArrayElementTypeNode(named.Type!) != null, ElementFlags.Rest, ElementFlags.Variadic);
                }
                return ElementFlags.Required;
        }
        return ElementFlags.Required;
    }

    internal TupleElementInfo GetTupleElementInfo(Node node)
    {
        return new TupleElementInfo
        {
            _flags = GetTupleElementFlags(node),
            _labeledDeclaration = IfElse<Node?>(IsNamedTupleMember(node) || IsParameterDeclaration(node), node, null),
        };
    }

    internal Type CreateTupleType(Type[] elementTypes)
    {
        var elementInfos = Map(elementTypes, _ => new TupleElementInfo { _flags = ElementFlags.Required });
        return CreateTupleTypeEx(elementTypes, elementInfos, false /*readonly*/);
    }

    internal Type CreateTupleTypeEx(Type[] elementTypes, TupleElementInfo[] elementInfos, bool @readonly)
    {
        var tupleTarget = GetTupleTargetType(elementInfos, @readonly);
        if (tupleTarget == _emptyGenericType)
        {
            return _emptyObjectType;
        }
        if (elementTypes.Length != 0)
        {
            return CreateNormalizedTypeReference(tupleTarget, elementTypes);
        }
        return tupleTarget;
    }

    internal Type GetTupleTargetType(TupleElementInfo[] elementInfos, bool @readonly)
    {
        if (elementInfos.Length == 1 && (elementInfos[0]._flags & ElementFlags.Rest) != 0)
        {
            // [...X[]] is equivalent to just X[]
            if (@readonly)
            {
                return _globalReadonlyArrayType;
            }
            return _globalArrayType;
        }
        var key = GetTupleKey(elementInfos, @readonly);
        var t = _tupleTypes.GetValueOrDefault(key);
        if (t == null)
        {
            t = CreateTupleTargetType(elementInfos, @readonly);
            _tupleTypes[key] = t;
        }
        return t;
    }

    // We represent tuple types as type references to synthesized generic interface types created by
    // this function. The types are of the form:
    //
    //     interface Tuple<T0, T1, T2, ...> extends Array<T0 | T1 | T2 | ...> { 0: T0, 1: T1, 2: T2, ... }
    //
    // Note that the generic type created by this function has no symbol associated with it. The same
    // is true for each of the synthesized type parameters.
    internal Type CreateTupleTargetType(TupleElementInfo[] elementInfos, bool @readonly)
    {
        var arity = elementInfos.Length;
        var minLength = CountWhere(elementInfos, e => (e._flags & (ElementFlags.Required | ElementFlags.Variadic)) != 0);
        var typeParameters = new List<Type>(arity);
        var members = new SymbolTable();
        var combinedFlags = ElementFlags.None;
        if (arity != 0)
        {
            for (var i = 0; i < arity; i++)
            {
                var typeParameter = NewTypeParameter(null);
                typeParameters.Add(typeParameter);
                var flags = elementInfos[i]._flags;
                combinedFlags |= flags;
                if ((combinedFlags & ElementFlags.Variable) == 0)
                {
                    var property = NewSymbolEx(SymbolFlags.Property | IfElse((flags & ElementFlags.Optional) != 0, SymbolFlags.Optional, (SymbolFlags)0), i.ToString(), IfElse(@readonly, CheckFlags.Readonly, (CheckFlags)0));
                    _valueSymbolLinks.Get(property)._resolvedType = typeParameter;
                    // _valueSymbolLinks.Get(property)._tupleLabelDeclaration = elementInfos[i]._labeledDeclaration
                    members[property.Name] = property;
                }
            }
        }
        var fixedLength = members.Count;
        var lengthSymbol = NewSymbolEx(SymbolFlags.Property, "length", IfElse(@readonly, CheckFlags.Readonly, (CheckFlags)0));
        if ((combinedFlags & ElementFlags.Variable) != 0)
        {
            _valueSymbolLinks.Get(lengthSymbol)._resolvedType = _numberType;
        }
        else
        {
            var literalTypes = new List<Type>();
            for (var i = minLength; i <= arity; i++)
            {
                literalTypes.Add(GetNumberLiteralType((Number)i));
            }
            _valueSymbolLinks.Get(lengthSymbol)._resolvedType = GetUnionTypeWorker(literalTypes.ToArray());
        }
        members[lengthSymbol.Name] = lengthSymbol;
        var t = NewObjectType(ObjectFlags.Tuple | ObjectFlags.Reference, null);
        var d = t.AsTupleType()!;
        d._thisType = NewTypeParameter(null);
        d._thisType!.AsTypeParameter()!._isThisType = true;
        d._thisType!.AsTypeParameter()!._constraint = t;
        typeParameters.Add(d._thisType!);
        d._allTypeParameters = typeParameters.ToArray();
        d._instantiations = new Dictionary<CacheHashKey, Type>();
        d._instantiations[GetTypeListKey(d.TypeParameters())] = t;
        d._target = t;
        d._resolvedTypeArguments = d.TypeParameters();
        d._declaredMembersResolved = true;
        d._declaredMembers = members;
        d._elementInfos = elementInfos;
        d._minLength = minLength;
        d._fixedLength = fixedLength;
        d._combinedFlags = combinedFlags;
        d._readonly = @readonly;
        return t;
    }

    internal Type? GetElementTypeOfSliceOfTupleType(Type t, int index, int endSkipCount, bool writing, bool noReductions)
    {
        var length = GetTypeReferenceArity(t) - endSkipCount;
        var elementInfos = t.TargetTupleType()!._elementInfos!;
        if (index < length)
        {
            var typeArguments = GetTypeArgumentsWorker(t);
            var elementTypes = new List<Type>();
            for (var i = index; i < length; i++)
            {
                var e = typeArguments[i];
                if ((elementInfos[i]._flags & ElementFlags.Variadic) != 0)
                {
                    e = GetIndexedAccessType(e, _numberType);
                }
                elementTypes.Add(e);
            }
            if (writing)
            {
                return GetIntersectionType(elementTypes.ToArray());
            }
            return GetUnionTypeExWorker(elementTypes.ToArray(), IfElse(noReductions, UnionReduction.None, UnionReduction.Literal), null, null);
        }
        return null;
    }

    internal Type? GetRestTypeOfTupleType(Type t)
    {
        return GetElementTypeOfSliceOfTupleType(t, t.TargetTupleType()!.FixedLength(), 0, false, false);
    }

    internal Type GetTupleElementTypeOutOfStartCount(Type t, Number index, Type? undefinedLikeType)
    {
        return MapType(t, tupleType =>
        {
            var restType = GetRestTypeOfTupleType(tupleType);
            if (restType == null)
            {
                return _undefinedType;
            }
            if (undefinedLikeType != null && index >= (Number)GetTotalFixedElementCount(tupleType.TargetTupleType()!))
            {
                return GetUnionTypeWorker(new Type[] { restType, undefinedLikeType });
            }
            return restType;
        });
    }

    internal bool IsGenericType(Type t)
    {
        return GetGenericObjectFlags(t) != 0;
    }

    internal bool IsGenericObjectType(Type t)
    {
        return (GetGenericObjectFlags(t) & ObjectFlags.IsGenericObjectType) != 0;
    }

    internal bool IsGenericIndexType(Type t)
    {
        return (GetGenericObjectFlags(t) & ObjectFlags.IsGenericIndexType) != 0;
    }

    internal ObjectFlags GetGenericObjectFlags(Type t)
    {
        var combinedFlags = ObjectFlags.None;
        if ((t._flags & (TypeFlags.UnionOrIntersection | TypeFlags.Substitution)) != 0)
        {
            if ((t._objectFlags & ObjectFlags.IsGenericTypeComputed) == 0)
            {
                if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
                {
                    foreach (var u in t.Types())
                    {
                        combinedFlags |= GetGenericObjectFlags(u);
                    }
                }
                else
                {
                    combinedFlags = GetGenericObjectFlags(t.AsSubstitutionType()!._baseType!) | GetGenericObjectFlags(t.AsSubstitutionType()!._constraint!);
                }
                t._objectFlags |= ObjectFlags.IsGenericTypeComputed | combinedFlags;
            }
            return t._objectFlags & ObjectFlags.IsGenericType;
        }
        if ((t._flags & TypeFlags.InstantiableNonPrimitive) != 0 || IsGenericMappedType(t) || IsGenericTupleType(t))
        {
            combinedFlags |= ObjectFlags.IsGenericObjectType;
        }
        if ((t._flags & (TypeFlags.InstantiableNonPrimitive | TypeFlags.Index)) != 0 || IsGenericStringLikeType(t))
        {
            combinedFlags |= ObjectFlags.IsGenericIndexType;
        }
        return combinedFlags;
    }

    // Go: `func (c *Checker) isGenericTupleType(t *Type) bool`（checker.go:25308）。Go 还有一个**同名包级函数**
    // （checker.go:23892，C6 已落地为 `Checker.C6.Tuples.cs` 的 `internal static bool IsGenericTupleType`）；
    // 按 Main 的裁决：实例方法保留 `IsGenericTupleType`，包级那个改名 `IsGenericTupleTypeWorker`（由 C6 片改）。
    internal bool IsGenericTupleType(Type t)
    {
        return IsTupleTypeWorker(t) && (t.TargetTupleType()!._combinedFlags & ElementFlags.Variadic) != 0;
    }

    internal bool IsGenericMappedType(Type t)
    {
        if ((t._objectFlags & ObjectFlags.Mapped) != 0)
        {
            var constraint = GetConstraintTypeFromMappedType(t);
            if (IsGenericIndexType(constraint))
            {
                return true;
            }
            // A mapped type is generic if the 'as' clause references generic types other than the iteration type.
            // To determine this, we substitute the constraint type (that we now know isn't generic) for the iteration
            // type and check whether the resulting type is generic.
            var nameType = GetNameTypeFromMappedType(t);
            if (nameType != null && IsGenericIndexType(InstantiateType(nameType, new SimpleTypeMapper(GetTypeParameterFromMappedType(t), constraint))))
            {
                return true;
            }
        }
        return false;
    }

    // A union type which is reducible upon instantiation (meaning some members are removed under certain instantiations)
    // must be kept generic, as that instantiation information needs to flow through the type system. By replacing all
    // type parameters in the union with a special never type that is treated as a literal in `getReducedType`, we can cause
    // the `getReducedType` logic to reduce the resulting type if possible (since only intersections with conflicting
    // literal-typed properties are reducible).
    internal bool IsGenericReducibleType(Type t)
    {
        return (t._flags & TypeFlags.Union) != 0 && (t._objectFlags & ObjectFlags.ContainsIntersections) != 0 && Some(t.Types(), IsGenericReducibleType) ||
            (t._flags & TypeFlags.Intersection) != 0 && IsReducibleIntersection(t);
    }

    internal bool IsReducibleIntersection(Type t)
    {
        var d = t.AsIntersectionType()!;
        if (d._uniqueLiteralFilledInstantiation == null)
        {
            d._uniqueLiteralFilledInstantiation = InstantiateType(t, _uniqueLiteralMapper);
        }
        return GetReducedTypeWorker(d._uniqueLiteralFilledInstantiation!) != d._uniqueLiteralFilledInstantiation;
    }

    internal Type GetUniqueLiteralTypeForTypeParameter(Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) != 0)
        {
            return _uniqueLiteralType;
        }
        return t;
    }

    internal Type GetConditionalFlowTypeOfType(Type t, Node? node)
    {
        var constraints = new List<Type>();
        var covariant = true;
        while (node != null && !IsStatement(node) && node.Kind != Kind.JSDoc)
        {
            var parent = node.Parent;
            // only consider variance flipped by parameter locations - `keyof` types would usually be considered variance inverting, but
            // often get used in indexed accesses where they behave sortof invariantly, but our checking is lax
            if (IsParameterDeclaration(parent!))
            {
                covariant = !covariant;
            }
            // Always substitute on type parameters, regardless of variance, since even
            // in contravariant positions, they may rely on substituted constraints to be valid
            if ((covariant || (t._flags & TypeFlags.TypeVariable) != 0) && IsConditionalTypeNode(parent!) && node == parent!.AsConditionalTypeNode()!.TrueType)
            {
                var constraint = GetImpliedConstraint(t, parent!.AsConditionalTypeNode()!.CheckType!, parent!.AsConditionalTypeNode()!.ExtendsType!);
                if (constraint != null)
                {
                    constraints.Add(constraint);
                }
            }
            else if ((t._flags & TypeFlags.TypeParameter) != 0 && IsMappedTypeNode(parent!) && parent!.AsMappedTypeNode()!.NameType == null && node == parent!.Type())
            {
                var mappedType = GetTypeFromTypeNodeWorker(parent!);
                if (GetTypeParameterFromMappedType(mappedType) == GetActualTypeVariable(t))
                {
                    var typeParameter = GetHomomorphicTypeVariable(mappedType);
                    if (typeParameter != null)
                    {
                        var constraint = GetConstraintOfTypeParameterWorker(typeParameter);
                        if (constraint != null && EveryType(constraint, IsArrayOrTupleType))
                        {
                            constraints.Add(GetUnionTypeWorker(new Type[] { _numberType, _numericStringType }));
                        }
                    }
                }
            }
            node = parent;
        }
        if (constraints.Count != 0)
        {
            return GetSubstitutionType(t, GetIntersectionType(constraints.ToArray()));
        }
        return t;
    }

    internal Type? GetImpliedConstraint(Type t, Node checkNode, Node extendsNode)
    {
        if (IsUnaryTupleTypeNode(checkNode) && IsUnaryTupleTypeNode(extendsNode))
        {
            return GetImpliedConstraint(t, checkNode.Elements()![0], extendsNode.Elements()![0]);
        }
        if (GetActualTypeVariable(GetTypeFromTypeNodeWorker(checkNode)) == GetActualTypeVariable(t))
        {
            return GetTypeFromTypeNodeWorker(extendsNode);
        }
        return null;
    }

    internal static bool IsUnaryTupleTypeNode(Node node)
    {
        return IsTupleTypeNode(node) && (node.Elements()?.Length ?? 0) == 1;
    }
}
