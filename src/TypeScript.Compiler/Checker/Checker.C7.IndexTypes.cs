// ported from: internal/checker/checker.go:27084-27404（keyof/索引类型 + 索引访问入口）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 包级函数 `isInvalidComputedPropertyName` → 同一个 `public sealed partial class Checker` 的 `internal static`
//    （docs/checker-plan.md §5.5；C# 没有自由函数）。
// 2) Go 未导出字段 → `_camelCase`（§5.5）：`t.flags`→`t._flags`、`t.objectFlags`→`t._objectFlags`、`t.id`→`t._id`、
//    `t.alias`→`t._alias`、`links.resolvedType`→`links._resolvedType`、`c.propertiesTypes`→`_propertiesTypes`、
//    `c.indexedAccessTypes`→`_indexedAccessTypes`、`c.cachedTypes`→`_cachedTypes`、`c.valueSymbolLinks`→`_valueSymbolLinks`、
//    `c.typeNodeLinks`→`_typeNodeLinks`、`c.compilerOptions`→`_compilerOptions`；`IndexInfo` 的 `keyType` 字段 →`_keyType`。
// 3) Checker 的 **func 字段**（Go 结构体字段，不是方法）：`c.getGlobalExtractSymbol()`→`_getGlobalExtractSymbol()`、
//    `c.isStringIndexSignatureOnlyType(t)`→`_isStringIndexSignatureOnlyType(t)`。
// 4) 同名方法冲突（artifacts/checker-method-collisions.md）：`c.getReducedType`→`GetReducedTypeWorker`、
//    `c.getUnionType`→`GetUnionTypeWorker`、`c.getUnionTypeEx`→`GetUnionTypeExWorker`、
//    `c.getPropertiesOfType`→`GetPropertiesOfTypeWorker`、`c.getIndexInfosOfType`→`GetIndexInfosOfTypeWorker`、
//    `c.getApparentType`→`GetApparentTypeWorker`、`c.isTypeAssignableTo`→`IsTypeAssignableToWorker`。
// 5) Go 里 lambda/内层函数的参数遮蔽外层同名参数（Go 允许，C# 是 CS0136）→ 内层改名 `x`：
//    `getIndexTypeEx` 的 `core.Map(t.Types(), func(t *Type))`、`getIndexTypeForMappedType` 的 `filterType(..., func(t))`。
// 6) Go 的 map 读缺失键返回零值 → `_indexedAccessTypes.GetValueOrDefault(key)`（§5.1；索引器会抛）。
// 7) `CacheHashKey` 是 readonly struct 且未定义 `==` → `getTypeListKey(...) == getTypeListKey(...)` 落成 `.Equals(...)`。
// 8) `ast.InternalSymbolName*` 是 `Ast` 静态类成员（`using static TypeScript.Ast.Ast;`）→ 裸名
//    `InternalSymbolNameDefault` / `InternalSymbolNameMissing`。
// 9) `t.AsSubstitutionType().baseType/.constraint` 在 C# 是 `Type?` 字段 → 需要处补 `!`；跨片 helper 返回值
//    nullability 未定处同样补 `!`（对非空声明是冗余但无害）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type GetIndexType(Type t)
    {
        return GetIndexTypeEx(t, IndexFlags.None);
    }

    internal Type GetIndexTypeEx(Type t, IndexFlags indexFlags)
    {
        t = GetReducedTypeWorker(t);
        if (IsNoInferType(t))
        {
            return GetNoInferType(GetIndexTypeEx(t.AsSubstitutionType()!._baseType!, indexFlags));
        }
        if (ShouldDeferIndexType(t, indexFlags))
        {
            return GetIndexTypeForGenericType(t, indexFlags);
        }
        if ((t._flags & TypeFlags.Union) != 0)
        {
            return GetIntersectionType(Map(t.Types(), x => GetIndexTypeEx(x, indexFlags)));
        }
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return GetUnionTypeWorker(Map(t.Types(), x => GetIndexTypeEx(x, indexFlags)));
        }
        if ((t._objectFlags & ObjectFlags.Mapped) != 0)
        {
            return GetIndexTypeForMappedType(t, indexFlags);
        }
        if (t == _wildcardType)
        {
            return _wildcardType;
        }
        if ((t._flags & TypeFlags.Unknown) != 0)
        {
            return _neverType;
        }
        if ((t._flags & (TypeFlags.Any | TypeFlags.Never)) != 0)
        {
            return _stringNumberSymbolType;
        }
        var include = IfElse((indexFlags & IndexFlags.NoIndexSignatures) != 0, TypeFlags.StringLiteral, TypeFlags.StringLike) |
            IfElse((indexFlags & IndexFlags.StringsOnly) != 0, TypeFlags.None, TypeFlags.NumberLike | TypeFlags.ESSymbolLike);
        return GetLiteralTypeFromProperties(t, include, indexFlags == IndexFlags.None);
    }

    internal Type GetExtractStringType(Type t)
    {
        var extractTypeAlias = _getGlobalExtractSymbol();
        if (extractTypeAlias != null)
        {
            return GetTypeAliasInstantiation(extractTypeAlias, [t, _stringType], null)!;
        }
        return _stringType;
    }

    internal Type GetLiteralTypeFromProperties(Type t, TypeFlags include, bool includeOrigin)
    {
        var key = new PropertiesTypesKey(
            t._id,
            include,
            includeOrigin,
            (t._objectFlags & ObjectFlags.UnresolvedMembers) != 0);
        if (_propertiesTypes.TryGetValue(key, out var cached))
        {
            return cached;
        }
        Type? origin = null;
        if (includeOrigin && (t._objectFlags & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0 || t._alias != null)
        {
            origin = NewIndexType(t, IndexFlags.None)!;
        }
        // Go 的 nil 切片 ↔ C# null（§5.1）：`len(props)`/`range props` 对 nil 视为空。
        var props = GetPropertiesOfTypeWorker(t) ?? [];
        var indexInfos = GetIndexInfosOfTypeWorker(t) ?? [];
        var types = new List<Type>(props.Length + indexInfos.Length);
        foreach (var prop in props)
        {
            types.Add(GetLiteralTypeFromProperty(prop, include, false));
        }
        foreach (var info in indexInfos)
        {
            if (info != _enumNumberIndexInfo && IsKeyTypeIncluded(info._keyType!, include))
            {
                if (info._keyType == _stringType && (include & TypeFlags.Number) != 0)
                {
                    types.Add(_stringOrNumberType);
                }
                else
                {
                    types.Add(info._keyType!);
                }
            }
        }
        var result = GetUnionTypeExWorker(types.ToArray(), UnionReduction.Literal, null, origin);
        _propertiesTypes[key] = result;
        return result;
    }

    internal Type GetLiteralTypeFromProperty(Symbol prop, TypeFlags include, bool includeNonPublic)
    {
        if (includeNonPublic || (GetDeclarationModifierFlagsFromSymbol(prop) & ModifierFlags.NonPublicAccessibilityModifier) == 0)
        {
            var t = _valueSymbolLinks.Get(GetLateBoundSymbol(prop))._nameType;
            if (t == null)
            {
                if (prop.Name == InternalSymbolNameDefault)
                {
                    t = GetStringLiteralType("default");
                }
                else
                {
                    var name = GetNameOfDeclaration(prop.ValueDeclaration);
                    if (name != null)
                    {
                        t = GetLiteralTypeFromPropertyName(name);
                    }
                    if (t == null && !IsKnownSymbol(prop))
                    {
                        t = GetStringLiteralType(SymbolName(prop));
                    }
                }
            }
            if (t != null && (t._flags & include) != 0)
            {
                return t;
            }
        }
        return _neverType;
    }

    internal Type GetLiteralTypeFromPropertyName(Node name)
    {
        if (IsPrivateIdentifier(name))
        {
            return _neverType;
        }
        if (IsNumericLiteral(name))
        {
            return GetRegularTypeOfLiteralType(CheckExpression(name));
        }
        if (IsComputedPropertyName(name))
        {
            return GetRegularTypeOfLiteralType(CheckComputedPropertyName(name));
        }
        var propertyName = GetPropertyNameForPropertyNameNode(name);
        if (propertyName != InternalSymbolNameMissing)
        {
            return GetStringLiteralType(propertyName);
        }
        if (IsExpression(name))
        {
            return GetRegularTypeOfLiteralType(CheckExpression(name));
        }
        return _neverType;
    }

    internal bool IsKeyTypeIncluded(Type keyType, TypeFlags include)
    {
        return (keyType._flags & include) != 0 ||
            (keyType._flags & TypeFlags.Intersection) != 0 && Some(keyType.Types(), x => IsKeyTypeIncluded(x, include));
    }

    internal static bool IsInvalidComputedPropertyName(Node node)
    {
        return (IsTypeLiteralNode(node.Parent!.Parent!) || IsClassLike(node.Parent!.Parent!) || IsInterfaceDeclaration(node.Parent!.Parent!)) &&
            IsBinaryExpression(node.Expression()!) && node.Expression()!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.InKeyword &&
            !IsAccessor(node.Parent);
    }

    internal Type CheckComputedPropertyName(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            links._resolvedType = _circularConstraintType;
            if (IsInvalidComputedPropertyName(node))
            {
                links._resolvedType = _errorType;
                return links._resolvedType;
            }
            links._resolvedType = CheckExpression(node.Expression()!);
            // This will allow types number, string, symbol or any. It will also allow enums, the unknown
            // type, and any union of these types (like string | number).
            if ((links._resolvedType._flags & TypeFlags.Nullable) != 0 ||
                !IsTypeAssignableToKind(links._resolvedType, TypeFlags.StringLike | TypeFlags.NumberLike | TypeFlags.ESSymbolLike) &&
                    !IsTypeAssignableToWorker(links._resolvedType, _stringNumberSymbolType))
            {
                Error(node, Diagnostics.A_computed_property_name_must_be_of_type_string_number_symbol_or_any);
            }
        }
        return links._resolvedType;
    }

    internal bool IsNoInferType(Type t)
    {
        // A NoInfer<T> type is represented as a substitution type with a TypeFlags.Unknown constraint.
        return (t._flags & TypeFlags.Substitution) != 0 && (t.AsSubstitutionType()!._constraint!._flags & TypeFlags.Unknown) != 0;
    }

    internal Type GetSubstitutionIntersection(Type t)
    {
        if (IsNoInferType(t))
        {
            return t.AsSubstitutionType()!._baseType!;
        }
        return GetIntersectionType([t.AsSubstitutionType()!._constraint!, t.AsSubstitutionType()!._baseType!]);
    }

    internal bool ShouldDeferIndexType(Type t, IndexFlags indexFlags)
    {
        return (t._flags & TypeFlags.InstantiableNonPrimitive) != 0 ||
            IsGenericTupleType(t) ||
            IsGenericMappedType(t) && GetNameTypeFromMappedType(t) != null ||
            (t._flags & TypeFlags.Union) != 0 && (indexFlags & IndexFlags.NoReducibleCheck) == 0 && IsGenericReducibleType(t) ||
            (t._flags & TypeFlags.Intersection) != 0 && MaybeTypeOfKind(t, TypeFlags.Instantiable) && Some(t.Types(), IsEmptyAnonymousObjectType);
    }

    internal MappedTypeNameTypeKind GetMappedTypeNameTypeKind(Type t)
    {
        var nameType = GetNameTypeFromMappedType(t);
        if (nameType == null)
        {
            return MappedTypeNameTypeKind.None;
        }
        if (IsTypeAssignableToWorker(nameType, GetTypeParameterFromMappedType(t)!))
        {
            return MappedTypeNameTypeKind.Filtering;
        }
        return MappedTypeNameTypeKind.Remapping;
    }

    internal Type GetIndexTypeForGenericType(Type t, IndexFlags indexFlags)
    {
        var key = new CachedTypeKey
        {
            _kind = IfElse((indexFlags & IndexFlags.StringsOnly) != 0, CachedTypeKind.StringIndexType, CachedTypeKind.IndexType),
            _typeId = t._id,
        };
        if (_cachedTypes.TryGetValue(key, out var indexType))
        {
            return indexType;
        }
        indexType = NewIndexType(t, indexFlags & IndexFlags.StringsOnly)!;
        _cachedTypes[key] = indexType;
        return indexType;
    }

    // This roughly mirrors `resolveMappedTypeMembers` in the nongeneric case, except only reports a union of the keys calculated,
    // rather than manufacturing the properties. We can't just fetch the `constraintType` since that would ignore mappings
    // and mapping the `constraintType` directly ignores how mapped types map _properties_ and not keys (thus ignoring subtype
    // reduction in the constraintType) when possible.
    // @param noIndexSignatures Indicates if _string_ index signatures should be elided. (other index signatures are always reported)
    internal Type GetIndexTypeForMappedType(Type t, IndexFlags indexFlags)
    {
        var typeParameter = GetTypeParameterFromMappedType(t)!;
        var constraintType = GetConstraintTypeFromMappedType(t)!;
        var nameType = GetNameTypeFromMappedType(OrElse(t.AsMappedType()!._target, t)!);
        if (nameType == null && (indexFlags & IndexFlags.NoIndexSignatures) == 0)
        {
            // no mapping and no filtering required, just quickly bail to returning the constraint in the common case
            return constraintType;
        }
        var keyTypes = new List<Type>();
        void AddMemberForKeyType(Type keyType)
        {
            var propNameType = keyType;
            if (nameType != null)
            {
                propNameType = InstantiateType(nameType, AppendTypeMapping(t.AsMappedType()!._mapper, typeParameter, keyType));
            }
            // `keyof` currently always returns `string | number` for concrete `string` index signatures - the below ternary keeps that behavior for mapped types
            // See `getLiteralTypeFromProperties` where there's a similar ternary to cause the same behavior.
            keyTypes.Add(IfElse(propNameType == _stringType, _stringOrNumberType, propNameType));
        }
        // Calling getApparentType on the `T` of a `keyof T` in the constraint type of a generic mapped type can
        // trigger a circularity. For example, `T extends { [P in keyof T & string as Captitalize<P>]: any }` is
        // a circular definition. For this reason, we only eagerly manifest the keys if the constraint is non-generic.
        if (IsGenericIndexType(constraintType))
        {
            if (IsMappedTypeWithKeyofConstraintDeclaration(t))
            {
                // We have a generic index and a homomorphic mapping and a key remapping - we need to defer
                // the whole `keyof whatever` for later since it's not safe to resolve the shape of modifier type.
                return GetIndexTypeForGenericType(t, indexFlags);
            }
            // Include the generic component in the resulting type.
            ForEachType(constraintType, AddMemberForKeyType);
        }
        else if (IsMappedTypeWithKeyofConstraintDeclaration(t))
        {
            var modifiersType = GetApparentTypeWorker(GetModifiersTypeFromMappedType(t));
            // The 'T' in 'keyof T'
            ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(modifiersType, TypeFlags.StringOrNumberLiteralOrUnique, (indexFlags & IndexFlags.StringsOnly) != 0, AddMemberForKeyType);
        }
        else
        {
            ForEachType(GetLowerBoundOfKeyType(constraintType), AddMemberForKeyType);
        }
        // We had to pick apart the constraintType to potentially map/filter it - compare the final resulting list with the
        // original constraintType, so we can return the union that preserves aliases/origin data if possible.
        Type result;
        if ((indexFlags & IndexFlags.NoIndexSignatures) != 0)
        {
            result = FilterType(GetUnionTypeWorker(keyTypes.ToArray()), x => (x._flags & (TypeFlags.Any | TypeFlags.String)) == 0);
        }
        else
        {
            result = GetUnionTypeWorker(keyTypes.ToArray());
        }
        if ((result._flags & TypeFlags.Union) != 0 && (constraintType._flags & TypeFlags.Union) != 0 && GetTypeListKey(result.Types()).Equals(GetTypeListKey(constraintType.Types())))
        {
            return constraintType;
        }
        return result;
    }

    internal Type GetIndexedAccessType(Type objectType, Type indexType)
    {
        return GetIndexedAccessTypeEx(objectType, indexType, AccessFlags.None, null, null);
    }

    internal Type GetIndexedAccessTypeEx(Type objectType, Type indexType, AccessFlags accessFlags, Node? accessNode, TypeAlias? alias)
    {
        var result = GetIndexedAccessTypeOrUndefined(objectType, indexType, accessFlags, accessNode, alias);
        if (result == null)
        {
            result = IfElse(accessNode != null, _errorType, _unknownType);
        }
        return result;
    }

    internal Type? GetIndexedAccessTypeOrUndefined(Type objectType, Type indexType, AccessFlags accessFlags, Node? accessNode, TypeAlias? alias)
    {
        if (objectType == _wildcardType || indexType == _wildcardType)
        {
            return _wildcardType;
        }
        objectType = GetReducedTypeWorker(objectType);
        // If the object type has a string index signature and no other members we know that the result will
        // always be the type of that index signature and we can simplify accordingly.
        if (_isStringIndexSignatureOnlyType(objectType) && (indexType._flags & TypeFlags.Nullable) == 0 && IsTypeAssignableToKind(indexType, TypeFlags.String | TypeFlags.Number))
        {
            indexType = _stringType;
        }
        // In noUncheckedIndexedAccess mode, indexed access operations that occur in an expression in a read position and resolve to
        // an index signature have 'undefined' included in their type.
        if (_compilerOptions.NoUncheckedIndexedAccess == Tristate.TSTrue && (accessFlags & AccessFlags.ExpressionPosition) != 0)
        {
            accessFlags |= AccessFlags.IncludeUndefined;
        }
        // If the index type is generic, or if the object type is generic and doesn't originate in an expression and
        // the operation isn't exclusively indexing the fixed (non-variadic) portion of a tuple type, we are performing
        // a higher-order index access where we cannot meaningfully access the properties of the object type. Note that
        // for a generic T and a non-generic K, we eagerly resolve T[K] if it originates in an expression. This is to
        // preserve backwards compatibility. For example, an element access 'this["foo"]' has always been resolved
        // eagerly using the constraint type of 'this' at the given location.
        if (ShouldDeferIndexedAccessType(objectType, indexType, accessNode))
        {
            if ((objectType._flags & TypeFlags.AnyOrUnknown) != 0)
            {
                return objectType;
            }
            // Defer the operation by creating an indexed access type.
            var persistentAccessFlags = accessFlags & AccessFlags.Persistent;
            var key = GetIndexedAccessKey(objectType, indexType, accessFlags, alias);
            var t = _indexedAccessTypes.GetValueOrDefault(key);
            if (t == null)
            {
                t = NewIndexedAccessType(objectType, indexType, persistentAccessFlags)!;
                t._alias = alias;
                _indexedAccessTypes[key] = t;
            }
            return t;
        }
        // In the following we resolve T[K] to the type of the property in T selected by K.
        // We treat boolean as different from other unions to improve errors;
        // skipping straight to getPropertyTypeForIndexType gives errors with 'boolean' instead of 'true'.
        var apparentObjectType = GetReducedApparentType(objectType);
        if ((indexType._flags & TypeFlags.Union) != 0 && (indexType._flags & TypeFlags.Boolean) == 0)
        {
            var propTypes = new List<Type>();
            var wasMissingProp = false;
            foreach (var t in indexType.Types())
            {
                var propType = GetPropertyTypeForIndexType(objectType, apparentObjectType, t, indexType, accessNode, accessFlags | IfElse(wasMissingProp, AccessFlags.SuppressNoImplicitAnyError, (AccessFlags)0));
                if (propType != null)
                {
                    propTypes.Add(propType);
                }
                else if (accessNode == null)
                {
                    // If there's no error node, we can immediately stop, since error reporting is off
                    return null;
                }
                else
                {
                    // Otherwise we set a flag and return at the end of the loop so we still mark all errors
                    wasMissingProp = true;
                }
            }
            if (wasMissingProp)
            {
                return null;
            }
            if ((accessFlags & AccessFlags.Writing) != 0)
            {
                return GetIntersectionTypeEx(propTypes.ToArray(), IntersectionFlags.None, alias);
            }
            return GetUnionTypeExWorker(propTypes.ToArray(), UnionReduction.Literal, alias, null);
        }
        return GetPropertyTypeForIndexType(objectType, apparentObjectType, indexType, indexType, accessNode, accessFlags | AccessFlags.CacheSymbol | AccessFlags.ReportDeprecated);
    }
}
