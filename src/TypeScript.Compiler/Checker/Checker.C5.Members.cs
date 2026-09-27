// ported from: internal/checker/checker.go:19184-19962（属性/成员/基类型解析：getPropertiesOfType…resolveDeclaredMembers）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 未导出方法命中 `artifacts/checker-method-collisions.md`（Go 导出 `GetX` 与未导出 `getX` 同名 → CS0111），
//    按 Main 裁决给未导出者加 `Worker` 后缀（先例 `EmitSourceFileWorker`），**定义点与调用点都用新名**：
//    getPropertiesOfType→GetPropertiesOfTypeWorker、getPropertyOfType→GetPropertyOfTypeWorker、
//    getTypeOfPropertyOfType→GetTypeOfPropertyOfTypeWorker、getSignaturesOfType→GetSignaturesOfTypeWorker、
//    getIndexInfosOfType→GetIndexInfosOfTypeWorker、getIndexInfoOfType→GetIndexInfoOfTypeWorker、
//    getIndexTypeOfType→GetIndexTypeOfTypeWorker、getBaseTypes→GetBaseTypesWorker。
// 2) 两处 C# 内部撞名：包级 `findIndexInfo`→`FindIndexInfoWorker`（实例版 `FindIndexInfo` 在 checker.go:20131）、
//    包级 `getTargetType`→`GetTargetTypeWorker`（实例版 `GetTargetType` 在 checker.go:31888）。
// 3) Go 的 nil slice：`T[]` 参数/返回一律写 `T[]?`（Go 区分 nil 与非 nil）；`len(x)` 对 nil 为 0 → 写
//    `(x?.Length ?? 0)` / `x is null || x.Length == 0`；`for range nilSlice` 不迭代 → `foreach (var v in x ?? [])`。
// 4) Go 的 map 读（缺键返回 nil）→ `GetValueOrDefault(k)`（§3.2；Binder 先例）；写/增用索引器。
// 5) `resolved.signatures[:resolved.callSignatureCount]` / `[resolved.callSignatureCount:]`（C# 数组没有切片视图）
//    → 直接调用 `StructuredType.CallSignatures()` / `ConstructSignatures()`（Go 里就是这两个访问器的实现体）。
// 6) `maps.Clone(members)`（nil→nil）→ 手工浅拷贝（`SymbolTable` 无复制构造器，且 `CopyMapInto` 返回
//    `Dictionary<,>` 无法赋回 `SymbolTable`）。
// 7) `core.OrElse(x, default)`（nil→default）在具体引用类型上等价于 `x ?? default`，且避免 OrElse 非空参数的 CS8604。
// 8) Go 把包级/方法当函数值传的地方：`core.Map(xs, c.getTypeFromTypeNode)` → `Map(xs, n => GetTypeFromTypeNodeWorker(n))`
//    （命中改名的一律写新名）；`core.Every(t.Types(), c.isValidBaseType)` → 同类内方法组直接转换。
// 9) `panic("...")` → `Debug.Fail("...")`（`TypeScript.Debug.Fail`，禁止 `using System.Diagnostics;`）。
// 10) Go 用闭包参数遮蔽外层变量（hasBaseType 的 `check = func(t *Type) bool`）在 C# 是 CS0136 → 局部函数参数改名。
// 11) `switch { case t.flags&X != 0: … }`（无标签 switch）→ `if/else if` 链（顺序与短路一致）；`switch len(x)` → `switch (x.Count)`。
// 12) `append(s, x)` → `Concatenate(s, new[] { x })`；`props = append(props, p)` → `List<T>` + `ToArray()`。
// 13) `sig.typeParameters` 传给**非空**参数（`GetMinTypeArgumentCountWorker`/`FillMissingTypeArgumentsWorker`）时写
//     `?? []`：Go 的 `len(nil slice) == 0` 在这两个函数里与空切片等价，且避免 CS8604/NRE。
// 14) `moduleSymbolLinks.Get(sym).typeOnlyExportStarMap[name]` 是 **nil map 读**（Go 返回 nil）→ C# 用
//     `_typeOnlyExportStarMap?.GetValueOrDefault(name)`（写成 `!` 会在 nil map 上抛 NRE，语义不同）。
// 15) `StructuredType.CallSignatures()/ConstructSignatures()` 在 C# 侧每次调用都分配（Types.Type.cs:357）→
//     `GetSingleSignature` 里先取局部量，避免同一分支重复分配（纯函数，结果不变）。
//
// 自查（Main/ChkC5 的提醒）：`node.Symbol`/`SubtreeFacts`/`ModifierFlags` 是**属性**（本文件用 `sig.declaration.Symbol`，
// 无括号）；`Expression()`/`TypeArguments()`/`Target()`/`Mapper()`/`Types()`/`TypeParameters()`/`CallSignatures()` 是方法；
// `Symbol.Name/Flags/Declarations/ValueDeclaration` 是字段。已 grep 自查：本文件 `.Symbol()` 命中 0、`'\xNN'` 命中 0。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Symbol[]? GetPropertiesOfTypeWorker(Type t)
    {
        t = GetReducedApparentType(t);
        if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            return GetPropertiesOfUnionOrIntersectionType(t);
        }
        return GetPropertiesOfObjectType(t);
    }

    internal Symbol[]? GetPropertiesOfObjectType(Type t)
    {
        if ((t._flags & TypeFlags.Object) != 0)
        {
            return ResolveStructuredTypeMembers(t)._properties;
        }
        return null;
    }

    internal Symbol[] GetPropertiesOfUnionOrIntersectionType(Type t)
    {
        var d = t.AsUnionOrIntersectionType()!;
        if (d._resolvedProperties is null)
        {
            // Go: var checked collections.Set[string]（零值可用）
            var checkedNames = new TypeScript.Collections.Set<string>();
            var props = new List<Symbol>();
            foreach (var current in d._types ?? [])
            {
                foreach (var prop in GetPropertiesOfTypeWorker(current) ?? [])
                {
                    if (!checkedNames.Has(prop.Name))
                    {
                        checkedNames.Add(prop.Name);
                        var combinedProp = GetPropertyOfUnionOrIntersectionType(t, prop.Name, (t._flags & TypeFlags.Intersection) != 0 /*skipObjectFunctionPropertyAugment*/);
                        if (combinedProp != null)
                        {
                            props.Add(combinedProp);
                        }
                    }
                }

                // The properties of a union type are those that are present in all constituent types, so
                // we only need to check the properties of the first type without index signature
                if ((t._flags & TypeFlags.Union) != 0 && (GetIndexInfosOfTypeWorker(current)?.Length ?? 0) == 0)
                {
                    break;
                }
            }

            d._resolvedProperties = props.ToArray();
        }

        return d._resolvedProperties!;
    }

    internal Symbol? GetPropertyOfTypeWorker(Type t, string name)
    {
        return GetPropertyOfTypeEx(t, name, false /*skipObjectFunctionPropertyAugment*/, false /*includeTypeOnlyMembers*/);
    }

    /// <summary>
    /// Return the symbol for the property with the given name in the given type. Creates synthetic union properties when
    /// necessary, maps primitive types and type parameters are to their apparent types, and augments with properties from
    /// Object and Function as appropriate.
    /// </summary>
    /// <param name="t">a type to look up property from</param>
    /// <param name="name">a name of property to look up in a given type</param>
    internal Symbol? GetPropertyOfTypeEx(Type t, string name, bool skipObjectFunctionPropertyAugment, bool includeTypeOnlyMembers)
    {
        t = GetReducedApparentType(t);
        if ((t._flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(t);
            var symbol = resolved._members?.GetValueOrDefault(name);
            if (symbol != null)
            {
                if (!includeTypeOnlyMembers && t._symbol != null && (t._symbol.Flags & SymbolFlags.ValueModule) != 0 && _moduleSymbolLinks.Get(t._symbol)._typeOnlyExportStarMap?.GetValueOrDefault(name) != null)
                {
                    // If this is the type of a module, `resolved.members.get(name)` might have effectively skipped over
                    // an `export type * from './foo'`, leaving `symbolIsValue` unable to see that the symbol is being
                    // viewed through a type-only export.
                    return null;
                }

                if (SymbolIsValueEx(symbol, includeTypeOnlyMembers))
                {
                    return symbol;
                }
            }

            if (skipObjectFunctionPropertyAugment)
            {
                return null;
            }

            Type? functionType = null;
            if (t == _anyFunctionType)
            {
                functionType = _globalFunctionType;
            }
            else if ((resolved.CallSignatures()?.Length ?? 0) != 0)
            {
                functionType = _globalCallableFunctionType;
            }
            else if ((resolved.ConstructSignatures()?.Length ?? 0) != 0)
            {
                functionType = _globalNewableFunctionType;
            }

            if (functionType != null)
            {
                symbol = GetPropertyOfObjectType(functionType, name);
                if (symbol != null)
                {
                    return symbol;
                }
            }

            return GetPropertyOfObjectType(_globalObjectType, name);
        }

        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            var prop = GetPropertyOfUnionOrIntersectionType(t, name, true /*skipObjectFunctionPropertyAugment*/);
            if (prop != null)
            {
                return prop;
            }

            if (!skipObjectFunctionPropertyAugment)
            {
                return GetPropertyOfUnionOrIntersectionType(t, name, skipObjectFunctionPropertyAugment);
            }

            return null;
        }

        if ((t._flags & TypeFlags.Union) != 0)
        {
            return GetPropertyOfUnionOrIntersectionType(t, name, skipObjectFunctionPropertyAugment);
        }

        return null;
    }

    // Return the type of the given property in the given type, or nil if no such property exists
    internal Type? GetTypeOfPropertyOfTypeWorker(Type t, string name)
    {
        var prop = GetPropertyOfTypeWorker(t, name);
        if (prop != null)
        {
            return GetTypeOfSymbolWorker(prop);
        }

        return null;
    }

    internal Signature[]? GetSignaturesOfTypeWorker(Type t, SignatureKind kind)
    {
        return GetSignaturesOfStructuredType(GetReducedApparentType(t), kind);
    }

    internal Signature[]? GetSignaturesOfStructuredType(Type t, SignatureKind kind)
    {
        if ((t._flags & TypeFlags.StructuredType) == 0)
        {
            return null;
        }

        var resolved = ResolveStructuredTypeMembers(t);
        if (kind == SignatureKind.Call)
        {
            return resolved.CallSignatures();
        }

        return resolved.ConstructSignatures();
    }

    // Go: `getIndexInfosOfType` / `getIndexInfosOfStructuredType` 返回 `[]*IndexInfo`，nil 切片与空切片在所有
    // 调用点（len/range/findIndexInfo）等价，且 Go 侧没有任何地方区分二者（`grep "indexInfos != nil"` 无命中）
    // → 归一成空数组，避免 C# 侧 `!` 解引用 nil。
    internal IndexInfo[] GetIndexInfosOfTypeWorker(Type t)
    {
        return GetIndexInfosOfStructuredType(GetReducedApparentType(t));
    }

    internal IndexInfo[] GetIndexInfosOfStructuredType(Type t)
    {
        if ((t._flags & TypeFlags.StructuredType) != 0)
        {
            return ResolveStructuredTypeMembers(t)._indexInfos ?? [];
        }

        return [];
    }

    // Return the indexing info of the given kind in the given type. Creates synthetic union index types when necessary and
    // maps primitive types and type parameters are to their apparent types.
    internal IndexInfo? GetIndexInfoOfTypeWorker(Type t, Type keyType)
    {
        return FindIndexInfoWorker(GetIndexInfosOfTypeWorker(t), keyType);
    }

    // Return the index type of the given kind in the given type. Creates synthetic union index types when necessary and
    // maps primitive types and type parameters are to their apparent types.
    internal Type? GetIndexTypeOfTypeWorker(Type t, Type keyType)
    {
        var info = GetIndexInfoOfTypeWorker(t, keyType);
        if (info != null)
        {
            return info._valueType;
        }

        return null;
    }

    internal Type? GetIndexTypeOfTypeEx(Type t, Type keyType, Type defaultType)
    {
        var result = GetIndexTypeOfTypeWorker(t, keyType);
        if (result != null)
        {
            return result;
        }

        return defaultType;
    }

    internal IndexInfo? GetApplicableIndexInfo(Type t, Type keyType)
    {
        return FindApplicableIndexInfo(GetIndexInfosOfTypeWorker(t), keyType);
    }

    internal IndexInfo? GetApplicableIndexInfoForName(Type t, string name)
    {
        if (IsLateBoundName(name))
        {
            return GetApplicableIndexInfo(t, _esSymbolType);
        }

        return GetApplicableIndexInfo(t, GetStringLiteralType(name));
    }

    internal IndexInfo? FindApplicableIndexInfo(IndexInfo[]? indexInfos, Type keyType)
    {
        // Index signatures for type 'string' are considered only when no other index signatures apply.
        IndexInfo? stringIndexInfo = null;
        var applicableInfos = new List<IndexInfo>(8);
        foreach (var info in indexInfos ?? [])
        {
            if (info._keyType == _stringType)
            {
                stringIndexInfo = info;
            }
            else if (IsApplicableIndexType(keyType, info._keyType!))
            {
                applicableInfos.Add(info);
            }
        }

        // When more than one index signature is applicable we create a synthetic IndexInfo. Instead of computing
        // the intersected key type, we just use unknownType for the key type as nothing actually depends on the
        // keyType property of the returned IndexInfo.
        switch (applicableInfos.Count)
        {
            case 0:
                if (stringIndexInfo != null && IsApplicableIndexType(keyType, _stringType))
                {
                    return stringIndexInfo;
                }

                return null;
            case 1:
                return applicableInfos[0];
            default:
                var isReadonly = true;
                var types = new Type[applicableInfos.Count];
                for (var i = 0; i < applicableInfos.Count; i++)
                {
                    var info = applicableInfos[i];
                    types[i] = info._valueType!;
                    if (!info._isReadonly)
                    {
                        isReadonly = false;
                    }
                }

                return NewIndexInfo(_unknownType, GetIntersectionType(types), isReadonly, null, null);
        }
    }

    internal bool IsApplicableIndexType(Type source, Type target)
    {
        // A 'string' index signature applies to types assignable to 'string' or 'number', and a 'number' index
        // signature applies to types assignable to 'number', `${number}` and numeric string literal types.
        return IsTypeAssignableToWorker(source, target) ||
            (target == _stringType && IsTypeAssignableToWorker(source, _numberType)) ||
            (target == _numberType && (source == _numericStringType || ((source._flags & TypeFlags.StringLiteral) != 0 && IsNumericLiteralName(GetStringLiteralValue(source)))));
    }

    internal StructuredType ResolveStructuredTypeMembers(Type t)
    {
        if ((t._objectFlags & ObjectFlags.MembersResolved) == 0)
        {
            if ((t._flags & TypeFlags.Object) != 0)
            {
                if ((t._objectFlags & ObjectFlags.Reference) != 0)
                {
                    ResolveTypeReferenceMembers(t);
                }
                else if ((t._objectFlags & ObjectFlags.ClassOrInterface) != 0)
                {
                    ResolveClassOrInterfaceMembers(t);
                }
                else if ((t._objectFlags & ObjectFlags.ReverseMapped) != 0)
                {
                    ResolveReverseMappedTypeMembers(t);
                }
                else if ((t._objectFlags & ObjectFlags.Anonymous) != 0)
                {
                    ResolveAnonymousTypeMembers(t);
                }
                else if ((t._objectFlags & ObjectFlags.Mapped) != 0)
                {
                    ResolveMappedTypeMembers(t);
                }
                else
                {
                    Debug.Fail("Unhandled case in resolveStructuredTypeMembers");
                }
            }
            else if ((t._flags & TypeFlags.Union) != 0)
            {
                ResolveUnionTypeMembers(t);
            }
            else if ((t._flags & TypeFlags.Intersection) != 0)
            {
                ResolveIntersectionTypeMembers(t);
            }
            else
            {
                Debug.Fail("Unhandled case in resolveStructuredTypeMembers");
            }
        }

        return t.AsStructuredType()!;
    }

    internal void ResolveClassOrInterfaceMembers(Type t)
    {
        ResolveObjectTypeMembers(t, t, null, null);
    }

    internal void ResolveTypeReferenceMembers(Type t)
    {
        var source = t.Target();
        var typeParameters = source!.AsInterfaceType()!._allTypeParameters;
        var typeArguments = GetTypeArgumentsWorker(t);
        var paddedTypeArguments = typeArguments;
        if ((typeArguments?.Length ?? 0) == (typeParameters?.Length ?? 0) - 1)
        {
            paddedTypeArguments = Concatenate(typeArguments, new[] { t });
        }

        ResolveObjectTypeMembers(t, source, typeParameters, paddedTypeArguments);
    }

    internal void ResolveObjectTypeMembers(Type t, Type source, Type[]? typeParameters, Type[]? typeArguments)
    {
        TypeMapper? mapper = null;
        SymbolTable? members = null;
        Signature[]? callSignatures = null;
        Signature[]? constructSignatures = null;
        IndexInfo[]? indexInfos = null;
        var instantiated = false;
        var resolved = ResolveDeclaredMembers(source);
        if (Same(typeParameters, typeArguments))
        {
            members = resolved._declaredMembers;
            callSignatures = resolved._declaredCallSignatures;
            constructSignatures = resolved._declaredConstructSignatures;
            indexInfos = resolved._declaredIndexInfos;
        }
        else
        {
            instantiated = true;
            mapper = NewTypeMapper(typeParameters!, typeArguments!);
            members = InstantiateSymbolTable(resolved._declaredMembers, mapper);
            callSignatures = InstantiateSignatures(resolved._declaredCallSignatures, mapper);
            constructSignatures = InstantiateSignatures(resolved._declaredConstructSignatures, mapper);
            indexInfos = InstantiateIndexInfos(resolved._declaredIndexInfos, mapper);
        }

        var baseTypes = GetBaseTypesWorker(source);
        if ((baseTypes?.Length ?? 0) != 0)
        {
            if (!instantiated)
            {
                // Go: maps.Clone(members)（members == nil → nil）
                if (members is not null)
                {
                    var clone = new SymbolTable();
                    foreach (var (key, value) in members)
                    {
                        clone[key] = value;
                    }

                    members = clone;
                }
            }

            SetStructuredTypeMembers(t, members, callSignatures, constructSignatures, indexInfos);
            var thisArgument = LastOrNil(typeArguments);
            t._objectFlags |= ObjectFlags.UnresolvedMembers;
            foreach (var baseType in baseTypes!)
            {
                var instantiatedBaseType = baseType;
                if (thisArgument != null)
                {
                    instantiatedBaseType = GetTypeWithThisArgument(InstantiateType(baseType, mapper!), thisArgument, false /*needsApparentType*/);
                }

                members = AddInheritedMembers(members, GetPropertiesOfTypeWorker(instantiatedBaseType));
                callSignatures = Concatenate(callSignatures, GetSignaturesOfTypeWorker(instantiatedBaseType, SignatureKind.Call));
                constructSignatures = Concatenate(constructSignatures, GetSignaturesOfTypeWorker(instantiatedBaseType, SignatureKind.Construct));
                IndexInfo[]? inheritedIndexInfos;
                if (instantiatedBaseType != _anyType)
                {
                    inheritedIndexInfos = GetIndexInfosOfTypeWorker(instantiatedBaseType);
                }
                else
                {
                    inheritedIndexInfos = new[] { _anyBaseTypeIndexInfo };
                }

                indexInfos = Concatenate(indexInfos, Filter(inheritedIndexInfos, info => FindIndexInfoWorker(indexInfos, info._keyType!) == null));
            }

            t._objectFlags &= ~ObjectFlags.UnresolvedMembers;
        }

        SetStructuredTypeMembers(t, members, callSignatures, constructSignatures, indexInfos);
    }

    internal static IndexInfo? FindIndexInfoWorker(IndexInfo[]? indexInfos, Type keyType)
    {
        foreach (var info in indexInfos ?? [])
        {
            if (info._keyType == keyType)
            {
                return info;
            }
        }

        return null;
    }

    internal Type[]? GetBaseTypesWorker(Type t)
    {
        if ((t._objectFlags & (ObjectFlags.ClassOrInterface | ObjectFlags.Tuple)) == 0)
        {
            return null;
        }

        var data = t.AsInterfaceType()!;
        if (!data._baseTypesResolved)
        {
            if (!PushTypeResolution(t, TypeSystemPropertyName.ResolvedBaseTypes))
            {
                return data._resolvedBaseTypes;
            }

            if ((t._objectFlags & ObjectFlags.Tuple) != 0)
            {
                data._resolvedBaseTypes = new[] { GetTupleBaseType(t) };
            }
            else if ((t._symbol!.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
            {
                if ((t._symbol!.Flags & SymbolFlags.Class) != 0)
                {
                    ResolveBaseTypesOfClass(t);
                }

                if ((t._symbol!.Flags & SymbolFlags.Interface) != 0)
                {
                    ResolveBaseTypesOfInterface(t);
                }
            }
            else
            {
                Debug.Fail("Unhandled case in getBaseTypes");
            }

            if (!PopTypeResolution() && t._symbol!.Declarations != null)
            {
                // Go: `for _, declaration := range t.symbol.Declarations`（checker.go:19528）
                foreach (var declaration in t._symbol!.Declarations ?? [])
                {
                    if (IsClassDeclaration(declaration) || IsInterfaceDeclaration(declaration))
                    {
                        ReportCircularBaseType(declaration, t);
                    }
                }
            }

            // In general, base type resolution always precedes member resolution. However, it is possible
            // for resolution of type parameter defaults to cause circularity errors, possibly leaving
            // members partially resolved. Here we ensure any such partial resolution is reset.
            // See https://github.com/microsoft/TypeScript/issues/16861 for an example.
            t._objectFlags &= ~ObjectFlags.MembersResolved;
            data._baseTypesResolved = true;
        }

        return data._resolvedBaseTypes;
    }

    internal Type GetTupleBaseType(Type t)
    {
        var typeParameters = t.AsTupleType()!.TypeParameters();
        TupleElementInfo[] elementInfos = t.AsTupleType()!._elementInfos!;
        var elementTypes = new Type[typeParameters?.Length ?? 0];
        for (var i = 0; i < elementTypes.Length; i++)
        {
            var tp = typeParameters![i];
            if ((elementInfos[i]._flags & ElementFlags.Variadic) != 0)
            {
                elementTypes[i] = GetIndexedAccessType(tp, _numberType);
            }
            else
            {
                elementTypes[i] = tp;
            }
        }

        return CreateArrayTypeEx(GetUnionTypeWorker(elementTypes), t.AsTupleType()!._readonly);
    }

    internal void ResolveBaseTypesOfClass(Type t)
    {
        var baseConstructorType = GetApparentTypeWorker(GetBaseConstructorTypeOfClassWorker(t));
        if ((baseConstructorType._flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.Any)) == 0)
        {
            return;
        }

        var baseTypeNode = GetBaseTypeNodeOfClass(t)!;
        Type? baseType = null;
        Type? originalBaseType = null;
        if (baseConstructorType._symbol != null)
        {
            originalBaseType = GetDeclaredTypeOfSymbolWorker(baseConstructorType._symbol);
        }

        if (baseConstructorType._symbol != null && (baseConstructorType._symbol.Flags & SymbolFlags.Class) != 0 && AreAllOuterTypeParametersApplied(originalBaseType!))
        {
            // When base constructor type is a class with no captured type arguments we know that the constructors all have the same type parameters as the
            // class and all return the instance type of the class. There is no need for further checks and we can apply the
            // type arguments in the same manner as a type reference to get the same error reporting experience.
            baseType = GetTypeFromClassOrInterfaceReference(baseTypeNode, baseConstructorType._symbol);
        }
        else if ((baseConstructorType._flags & TypeFlags.Any) != 0)
        {
            baseType = baseConstructorType;
        }
        else
        {
            // The class derives from a "class-like" constructor function, check that we have at least one construct signature
            // with a matching number of type parameters and use the return type of the first instantiated signature. Elsewhere
            // we check that all instantiated signatures return the same type.
            var constructors = GetInstantiatedConstructorsForTypeArguments(baseConstructorType, baseTypeNode.TypeArguments(), baseTypeNode);
            if (constructors.Length == 0)
            {
                Error(baseTypeNode.Expression(), Diagnostics.No_base_constructor_has_the_specified_number_of_type_arguments);
                return;
            }

            baseType = GetReturnTypeOfSignatureWorker(constructors[0]);
        }

        if (IsErrorType(baseType!))
        {
            return;
        }

        var reducedBaseType = GetReducedTypeWorker(baseType!);
        if (!IsValidBaseType(reducedBaseType))
        {
            var errorNode = baseTypeNode.Expression();
            var diagnostic = ElaborateNeverIntersection(null, errorNode!, baseType!);
            diagnostic = NewDiagnosticChainForNode(diagnostic, errorNode!, Diagnostics.Base_constructor_return_type_0_is_not_an_object_type_or_intersection_of_object_types_with_statically_known_members, TypeToString(reducedBaseType));
            AddDiagnostic(diagnostic);
            return;
        }

        if (t == reducedBaseType || HasBaseType(reducedBaseType, t))
        {
            Error(t._symbol!.ValueDeclaration, Diagnostics.Type_0_recursively_references_itself_as_a_base_type, TypeToString(t));
            return;
        }

        t.AsInterfaceType()!._resolvedBaseTypes = new[] { reducedBaseType };
    }

    internal static Node? GetBaseTypeNodeOfClass(Type t)
    {
        var decl = GetClassLikeDeclarationOfSymbol(t._symbol);
        if (decl != null)
        {
            return GetClassExtendsHeritageElement(decl);
        }

        return null;
    }

    internal Signature[] GetInstantiatedConstructorsForTypeArguments(Type t, Node[]? typeArgumentNodes, Node location)
    {
        var signatures = GetConstructorsForTypeArguments(t, typeArgumentNodes, location);
        var typeArguments = Map(typeArgumentNodes, n => GetTypeFromTypeNodeWorker(n));
        return SameMap(signatures, sig =>
        {
            if ((sig._typeParameters?.Length ?? 0) != 0)
            {
                return GetSignatureInstantiation(sig, typeArguments, IsInJSFile(location), null);
            }

            return sig;
        });
    }

    internal Signature[] GetConstructorsForTypeArguments(Type t, Node[]? typeArgumentNodes, Node location)
    {
        var typeArgCount = typeArgumentNodes?.Length ?? 0;
        return Filter(GetSignaturesOfTypeWorker(t, SignatureKind.Construct), sig =>
            typeArgCount >= GetMinTypeArgumentCountWorker(sig._typeParameters ?? []) && typeArgCount <= (sig._typeParameters?.Length ?? 0));
    }

    internal Signature GetSignatureInstantiation(Signature sig, Type[]? typeArguments, bool isJavaScript, Type[]? inferredTypeParameters)
    {
        var instantiatedSignature = GetSignatureInstantiationWithoutFillingInTypeArguments(sig, FillMissingTypeArgumentsWorker(typeArguments, sig._typeParameters ?? [], GetMinTypeArgumentCountWorker(sig._typeParameters ?? []), isJavaScript));
        if ((inferredTypeParameters?.Length ?? 0) != 0)
        {
            var returnSignature = GetSingleCallOrConstructSignature(GetReturnTypeOfSignatureWorker(instantiatedSignature));
            if (returnSignature != null)
            {
                var newReturnSignature = CloneSignature(returnSignature);
                newReturnSignature._typeParameters = inferredTypeParameters;
                var newReturnType = GetOrCreateTypeFromSignature(newReturnSignature);
                newReturnType.AsObjectType()!._mapper = instantiatedSignature._mapper;
                var newInstantiatedSignature = CloneSignature(instantiatedSignature);
                newInstantiatedSignature._resolvedReturnType = newReturnType;
                return newInstantiatedSignature;
            }
        }

        return instantiatedSignature;
    }

    internal Signature CloneSignature(Signature sig)
    {
        var result = NewSignature(sig._flags & SignatureFlags.PropagatingFlags, sig._declaration, sig._typeParameters, sig._thisParameter, sig._parameters, null, null, sig._minArgumentCount);
        result._target = sig._target;
        result._mapper = sig._mapper;
        result._composite = sig._composite;
        return result;
    }

    internal Signature GetSignatureInstantiationWithoutFillingInTypeArguments(Signature sig, Type[]? typeArguments)
    {
        var key = new CachedSignatureKey(sig, GetTypeListKey(typeArguments));
        Signature? instantiation = _cachedSignatures.GetValueOrDefault(key);
        if (instantiation == null)
        {
            instantiation = CreateSignatureInstantiation(sig, typeArguments);
            _cachedSignatures[key] = instantiation;
        }

        return instantiation;
    }

    internal Signature CreateSignatureInstantiation(Signature sig, Type[]? typeArguments)
    {
        return InstantiateSignatureEx(sig, CreateSignatureTypeMapper(sig, typeArguments), true /*eraseTypeParameters*/);
    }

    internal TypeMapper CreateSignatureTypeMapper(Signature sig, Type[]? typeArguments)
    {
        return NewTypeMapper(GetTypeParametersForMapper(sig), typeArguments ?? []);
    }

    internal Type[] GetTypeParametersForMapper(Signature sig)
    {
        return SameMap(sig._typeParameters, tp => InstantiateType(tp, tp.Mapper()!));
    }

    // If type has a single call signature and no other members, return that signature. Otherwise, return nil.
    internal Signature? GetSingleCallSignature(Type t)
    {
        return GetSingleSignature(t, SignatureKind.Call, false /*allowMembers*/);
    }

    internal Signature? GetSingleCallOrConstructSignature(Type t)
    {
        var callSig = GetSingleSignature(t, SignatureKind.Call, false /*allowMembers*/);
        if (callSig != null)
        {
            return callSig;
        }

        return GetSingleSignature(t, SignatureKind.Construct, false /*allowMembers*/);
    }

    internal Signature? GetSingleSignature(Type t, SignatureKind kind, bool allowMembers)
    {
        if ((t._flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(t);
            if (allowMembers || ((resolved._properties?.Length ?? 0) == 0 && (resolved._indexInfos?.Length ?? 0) == 0))
            {
                // Go 的 CallSignatures()/ConstructSignatures() 是零拷贝切片视图；C# 的访问器每次调用都会分配
                // （Types.Type.cs `_signatures[.._callSignatureCount]`）→ 先取局部量，避免同一分支重复分配
                // （纯函数，求值顺序对结果无影响）。
                var callSignatures = resolved.CallSignatures();
                var constructSignatures = resolved.ConstructSignatures();
                if (kind == SignatureKind.Call && callSignatures.Length == 1 && constructSignatures.Length == 0)
                {
                    return callSignatures[0];
                }

                if (kind == SignatureKind.Construct && constructSignatures.Length == 1 && callSignatures.Length == 0)
                {
                    return constructSignatures[0];
                }
            }
        }

        return null;
    }

    internal Type GetOrCreateTypeFromSignature(Signature sig)
    {
        // There are two ways to declare a construct signature, one is by declaring a class constructor
        // using the constructor keyword, and the other is declaring a bare construct signature in an
        // object type literal or interface (using the new keyword). Each way of declaring a constructor
        // will result in a different declaration kind.
        if (sig._isolatedSignatureType == null)
        {
            var kind = Kind.Unknown;
            if (sig._declaration != null)
            {
                kind = sig._declaration.Kind;
            }

            // If declaration is undefined, it is likely to be the signature of the default constructor.
            var isConstructor = kind == Kind.Unknown || kind == Kind.Constructor || kind == Kind.ConstructSignature || kind == Kind.ConstructorType;

            Symbol? symbol = null;
            if (sig._declaration != null)
            {
                symbol = sig._declaration.Symbol;
            }

            var t = NewObjectType(ObjectFlags.Anonymous | ObjectFlags.SingleSignatureType, symbol);
            if (isConstructor)
            {
                SetStructuredTypeMembers(t, null, null, new[] { sig }, null);
            }
            else
            {
                SetStructuredTypeMembers(t, null, new[] { sig }, null, null);
            }

            sig._isolatedSignatureType = t;
        }

        return sig._isolatedSignatureType!;
    }

    internal Signature GetErasedSignature(Signature signature)
    {
        if ((signature._typeParameters?.Length ?? 0) == 0)
        {
            return signature;
        }

        var key = new CachedSignatureKey(signature, SignatureKeyErased);
        Signature? erased = _cachedSignatures.GetValueOrDefault(key);
        if (erased == null)
        {
            erased = InstantiateSignatureEx(signature, NewArrayToSingleTypeMapper(signature._typeParameters!, _anyType), true /*eraseTypeParameters*/);
            _cachedSignatures[key] = erased;
        }

        return erased;
    }

    internal Signature GetCanonicalSignature(Signature signature)
    {
        if ((signature._typeParameters?.Length ?? 0) == 0)
        {
            return signature;
        }

        var key = new CachedSignatureKey(signature, SignatureKeyCanonical);
        Signature? canonical = _cachedSignatures.GetValueOrDefault(key);
        if (canonical == null)
        {
            canonical = CreateCanonicalSignature(signature);
            _cachedSignatures[key] = canonical;
        }

        return canonical;
    }

    internal Signature CreateCanonicalSignature(Signature signature)
    {
        // Create an instantiation of the signature where each unconstrained type parameter is replaced with
        // its original. When a generic class or interface is instantiated, each generic method in the class or
        // interface is instantiated with a fresh set of cloned type parameters (which we need to handle scenarios
        // where different generations of the same type parameter are in scope). This leads to a lot of new type
        // identities, and potentially a lot of work comparing those identities, so here we create an instantiation
        // that uses the original type identities for all unconstrained type parameters.
        var typeArguments = Map(signature._typeParameters, tp =>
        {
            var target = tp.Target();
            if (target != null && GetConstraintOfTypeParameterWorker(target) == null)
            {
                return target;
            }

            return tp;
        });
        return GetSignatureInstantiation(signature, typeArguments, IsInJSFile(signature._declaration), null /*inferredTypeParameters*/);
    }

    internal Signature GetBaseSignature(Signature signature)
    {
        var typeParameters = signature._typeParameters;
        if (typeParameters is null || typeParameters.Length == 0)
        {
            return signature;
        }

        var key = new CachedSignatureKey(signature, SignatureKeyBase);
        Signature? cached = _cachedSignatures.GetValueOrDefault(key);
        if (cached != null)
        {
            return cached;
        }

        var baseConstraintMapper = NewTypeMapper(typeParameters, Map(typeParameters, tp => GetConstraintOfTypeParameterWorker(tp) ?? _unknownType));
        var baseConstraints = Map(typeParameters, tp => InstantiateType(tp, baseConstraintMapper));

        // Run the immediate constraint mapper N-1 times so non-circular interdependent type parameters
        // resolve to their external dependencies without adding an extra expansion step for self-recursive constraints.
        for (var i = 0; i < typeParameters.Length - 1; i++)
        {
            baseConstraints = InstantiateTypes(baseConstraints, baseConstraintMapper);
        }

        // and then apply a type eraser to remove any remaining circularly dependent type parameters
        baseConstraints = InstantiateTypes(baseConstraints, NewArrayToSingleTypeMapper(typeParameters, _anyType));
        var result = InstantiateSignatureEx(signature, NewTypeMapper(typeParameters, baseConstraints), true /*eraseTypeParameters*/);
        _cachedSignatures[key] = result;
        return result;
    }

    // Instantiate a generic signature in the context of a non-generic signature (section 3.8.5 in TypeScript spec)
    internal Signature InstantiateSignatureInContextOf(Signature signature, Signature contextualSignature, InferenceContext? inferenceContext, TypeComparer? compareTypes)
    {
        var context = NewInferenceContext(GetTypeParametersForMapper(signature), signature, InferenceFlags.None, compareTypes);

        // We clone the inferenceContext to avoid fixing. For example, when the source signature is <T>(x: T) => T[] and
        // the contextual signature is (...args: A) => B, we want to infer the element type of A's constraint (say 'any')
        // for T but leave it possible to later infer '[any]' back to A.
        var restType = GetEffectiveRestType(contextualSignature);
        TypeMapper? mapper = null;
        if (inferenceContext != null)
        {
            if (restType != null && (restType._flags & TypeFlags.TypeParameter) != 0)
            {
                mapper = inferenceContext._nonFixingMapper;
            }
            else
            {
                mapper = inferenceContext._mapper;
            }
        }

        Signature sourceSignature;
        if (mapper != null)
        {
            sourceSignature = InstantiateSignature(contextualSignature, mapper);
        }
        else
        {
            sourceSignature = contextualSignature;
        }

        ApplyToParameterTypes(sourceSignature, signature, (source, target) =>
        {
            // Type parameters from outer context referenced by source type are fixed by instantiation of the source type
            InferTypes(context._inferences, source, target, InferencePriority.None, false);
        });
        if (inferenceContext == null)
        {
            ApplyToReturnTypes(contextualSignature, signature, (source, target) =>
            {
                InferTypes(context._inferences, source, target, InferencePriority.ReturnType, false);
            });
        }

        return GetSignatureInstantiation(signature, GetInferredTypes(context), IsInJSFile(contextualSignature._declaration), null /*inferredTypeParameters*/);
    }

    internal void ResolveBaseTypesOfInterface(Type t)
    {
        var data = t.AsInterfaceType()!;
        foreach (var declaration in t._symbol!.Declarations ?? [])
        {
            if (IsInterfaceDeclaration(declaration))
            {
                foreach (var node in GetExtendsHeritageClauseElements(declaration))
                {
                    var baseType = GetReducedTypeWorker(GetTypeFromTypeNodeWorker(node));
                    if (!IsErrorType(baseType))
                    {
                        if (IsValidBaseType(baseType))
                        {
                            if (t != baseType && !HasBaseType(baseType, t))
                            {
                                data._resolvedBaseTypes = Concatenate(data._resolvedBaseTypes, new[] { baseType });
                            }
                            else
                            {
                                ReportCircularBaseType(declaration, t);
                            }
                        }
                        else
                        {
                            Error(node, Diagnostics.An_interface_can_only_extend_an_object_type_or_intersection_of_object_types_with_statically_known_members);
                        }
                    }
                }
            }
        }
    }

    internal bool AreAllOuterTypeParametersApplied(Type t)
    {
        // An unapplied type parameter has its symbol still the same as the matching argument symbol.
        // Since parameters are applied outer-to-inner, only the last outer parameter needs to be checked.
        var outerTypeParameters = t.AsInterfaceType()!.OuterTypeParameters();
        if (outerTypeParameters is not null && outerTypeParameters.Length != 0)
        {
            var last = outerTypeParameters.Length - 1;
            var typeArguments = GetTypeArgumentsWorker(t);
            return outerTypeParameters[last]._symbol != typeArguments[last]._symbol;
        }

        return true;
    }

    internal void ReportCircularBaseType(Node node, Type t)
    {
        Error(node, Diagnostics.Type_0_recursively_references_itself_as_a_base_type, TypeToStringExWorker(t, null, TypeFormatFlags.WriteArrayAsGenericType, null));
    }

    // A valid base type is `any`, an object type or intersection of object types.
    internal bool IsValidBaseType(Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) != 0)
        {
            var constraint = GetBaseConstraintOfTypeWorker(t);
            if (constraint != null)
            {
                return IsValidBaseType(constraint);
            }
        }

        // TODO: Given that we allow type parameters here now, is this `!isGenericMappedType(type)` check really needed?
        // There's no reason a `T` should be allowed while a `Readonly<T>` should not.
        return (((t._flags & (TypeFlags.Object | TypeFlags.NonPrimitive | TypeFlags.Any)) != 0 && !IsGenericMappedType(t)) ||
            ((t._flags & TypeFlags.Intersection) != 0 && Every(t.Types(), IsValidBaseType)));
    }

    // TODO: GH#18217 If `checkBase` is undefined, we should not call this because this will always return false.
    internal bool HasBaseType(Type t, Type? checkBase)
    {
        bool Check(Type current)
        {
            if ((current._objectFlags & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
            {
                var target = GetTargetTypeWorker(current);
                return target == checkBase || Some(GetBaseTypesWorker(target), Check);
            }

            if ((current._flags & TypeFlags.Intersection) != 0)
            {
                return Some(current.Types(), Check);
            }

            return false;
        }

        return Check(t);
    }

    internal static Type GetTargetTypeWorker(Type t)
    {
        if ((t._objectFlags & ObjectFlags.Reference) != 0)
        {
            return t.Target()!;
        }

        return t;
    }

    internal Type GetTypeWithThisArgument(Type t, Type? thisArgument, bool needApparentType)
    {
        if ((t._objectFlags & ObjectFlags.Reference) != 0)
        {
            var target = t.Target()!;
            var typeArguments = GetTypeArgumentsWorker(t);
            if ((target.AsInterfaceType()!.TypeParameters()?.Length ?? 0) == (typeArguments?.Length ?? 0))
            {
                if (thisArgument == null)
                {
                    thisArgument = target.AsInterfaceType()!._thisType;
                }

                return CreateTypeReference(target, Concatenate(typeArguments, new[] { thisArgument! }));
            }

            return t;
        }

        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            var types = t.Types();
            var newTypes = SameMap(types, x => GetTypeWithThisArgument(x, thisArgument, needApparentType));
            if (Same(newTypes, types))
            {
                return t;
            }

            return GetIntersectionType(newTypes);
        }

        if (needApparentType)
        {
            return GetApparentTypeWorker(t);
        }

        return t;
    }

    internal SymbolTable? AddInheritedMembers(SymbolTable? symbols, Symbol[]? baseSymbols)
    {
        foreach (var baseSymbol in baseSymbols ?? [])
        {
            if (!IsStaticPrivateIdentifierProperty(baseSymbol))
            {
                var existing = symbols?.GetValueOrDefault(baseSymbol.Name);
                if (existing is null || (existing.Flags & SymbolFlags.Value) == 0)
                {
                    symbols ??= new SymbolTable();
                    symbols[baseSymbol.Name] = baseSymbol;
                }
            }
        }

        return symbols;
    }

    internal InterfaceType ResolveDeclaredMembers(Type t)
    {
        var d = t.AsInterfaceType()!;
        if (!d._declaredMembersResolved)
        {
            var members = GetMembersOfSymbol(t._symbol!);
            d._declaredMembersResolved = true;
            d._declaredMembers = members;
            d._declaredCallSignatures = GetSignaturesOfSymbol(d._declaredMembers!.GetValueOrDefault(InternalSymbolNameCall));
            d._declaredConstructSignatures = GetSignaturesOfSymbol(d._declaredMembers!.GetValueOrDefault(InternalSymbolNameNew));
            d._declaredIndexInfos = GetIndexInfosOfSymbol(t._symbol!);
        }

        return d;
    }
}
