// ported from: internal/checker/checker.go:27997-28490（kind 判定 + 属性比较/引用标记 + 类型规范化与化简）
//
// 与 Go 的偏离（逐条）：
// 1) 未导出方法 `typeHasCallOrConstructSignatures` 与 exports.go 的导出 `TypeHasCallOrConstructSignatures` 同名 →
//    未导出者加 `Worker` 后缀（artifacts/checker-method-collisions.md）。
// 2) Go 的包级函数 `isConstEnumObjectType`/`isConstEnumSymbol`/`compareTypesEqual`/`hasRestParameter`/`isRestParameter`/
//    `getNameFromIndexInfo` → 本 partial class 的 `internal static` 成员（名字按 C# 习惯 PascalCase）。
// 3) `if cached := c.cachedTypes[key]; cached != nil` → `TryGetValue`（Go map 读缺失键返回零值，C# 索引器会抛）。
// 4) Go 里 `for _, t := range t.Types()` / 闭包形参 `t` 遮蔽外层 `t` 的写法，C# 不允许同名遮蔽 → 内层改名 `u`。
// 5) `expandSignatureParametersWithTupleMembers` 的 `append(make(...), signature.parameters[:restIndex]...)` →
//    `new List<Symbol>(signature._parameters![..restIndex])`，末尾 `.ToArray()` 还原 Go 的 `[]*ast.Symbol`。
// 6) `core.Some(types, c.IsEmptyAnonymousObjectType)` → 同类实例方法组直接转委托（`Some(types, IsEmptyAnonymousObjectType)`）。
// 7) `getNormalizedType` 里 `n = c.getSingleBaseForNonAugmentingSubtype(t); if n == nil { n = t }` →
//    `var singleBase = ...; n = singleBase != null ? singleBase : t;`（Go 的 `*Type` 在 C# 是 `Type?`，
//    直接赋给非空 `Type n` 会触发可空性警告，而本仓 TreatWarningsAsErrors=true）。
// 8) `[]RecursionId` → `List<RecursionId>?`：与同片 `getResolvedBaseConstraint`/`computeBaseConstraint`
//    （Checker.C7.IndexedAccess.cs:620/676，Go 里对 stack 做 append）的既有落法保持一致。

namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal Type? GetNextBaseConstraint(Type? t, List<RecursionId>? stack)
    {
        if (t is null)
        {
            return null;
        }
        var constraint = GetResolvedBaseConstraint(t, stack);
        if (constraint == _noConstraintType || constraint == _circularConstraintType)
        {
            return null;
        }
        return constraint;
    }

    // Return true if type might be of the given kind. A union or intersection type might be of a given
    // kind if at least one constituent type is of the given kind.
    internal bool MaybeTypeOfKind(Type t, TypeFlags kind)
    {
        if ((t._flags & kind) != 0)
        {
            return true;
        }
        if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            foreach (var u in t.Types())
            {
                if (MaybeTypeOfKind(u, kind))
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal bool MaybeTypeOfKindConsideringBaseConstraint(Type t, TypeFlags kind)
    {
        if (MaybeTypeOfKind(t, kind))
        {
            return true;
        }
        var baseConstraint = GetBaseConstraintOrType(t);
        return baseConstraint != null && MaybeTypeOfKind(baseConstraint, kind);
    }

    internal bool AllTypesAssignableToKind(Type source, TypeFlags kind)
    {
        return AllTypesAssignableToKindEx(source, kind, false);
    }

    internal bool AllTypesAssignableToKindEx(Type source, TypeFlags kind, bool strict)
    {
        if ((source._flags & TypeFlags.Union) != 0)
        {
            return Every(source.Types(), subType => AllTypesAssignableToKindEx(subType, kind, strict));
        }
        return IsTypeAssignableToKindEx(source, kind, strict);
    }

    internal bool IsTypeAssignableToKind(Type source, TypeFlags kind)
    {
        return IsTypeAssignableToKindEx(source, kind, false);
    }

    internal bool IsTypeAssignableToKindEx(Type source, TypeFlags kind, bool strict)
    {
        if ((source._flags & kind) != 0)
        {
            return true;
        }
        if (strict && (source._flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Null)) != 0)
        {
            return false;
        }
        return (kind & TypeFlags.NumberLike) != 0 && IsTypeAssignableToWorker(source, _numberType)
            || (kind & TypeFlags.BigIntLike) != 0 && IsTypeAssignableToWorker(source, _bigintType)
            || (kind & TypeFlags.StringLike) != 0 && IsTypeAssignableToWorker(source, _stringType)
            || (kind & TypeFlags.BooleanLike) != 0 && IsTypeAssignableToWorker(source, _booleanType)
            || (kind & TypeFlags.Void) != 0 && IsTypeAssignableToWorker(source, _voidType)
            || (kind & TypeFlags.Never) != 0 && IsTypeAssignableToWorker(source, _neverType)
            || (kind & TypeFlags.Null) != 0 && IsTypeAssignableToWorker(source, _nullType)
            || (kind & TypeFlags.Undefined) != 0 && IsTypeAssignableToWorker(source, _undefinedType)
            || (kind & TypeFlags.ESSymbol) != 0 && IsTypeAssignableToWorker(source, _esSymbolType)
            || (kind & TypeFlags.NonPrimitive) != 0 && IsTypeAssignableToWorker(source, _nonPrimitiveType);
    }

    internal static bool IsConstEnumObjectType(Type t)
    {
        return (t._objectFlags & ObjectFlags.Anonymous) != 0 && t._symbol != null && IsConstEnumSymbol(t._symbol);
    }

    internal static bool IsConstEnumSymbol(Symbol? symbol)
    {
        return (symbol!.Flags & SymbolFlags.ConstEnum) != 0;
    }

    internal Ternary CompareProperties(Symbol sourceProp, Symbol targetProp, TypeRelatedComparer compareTypes)
    {
        // Two members are considered identical when
        // - they are public properties with identical names, optionality, and types,
        // - they are private or protected properties originating in the same declaration and having identical types
        if (sourceProp == targetProp)
        {
            return Ternary.True;
        }
        var sourcePropAccessibility = GetDeclarationModifierFlagsFromSymbol(sourceProp) & ModifierFlags.NonPublicAccessibilityModifier;
        var targetPropAccessibility = GetDeclarationModifierFlagsFromSymbol(targetProp) & ModifierFlags.NonPublicAccessibilityModifier;
        if (sourcePropAccessibility != targetPropAccessibility)
        {
            return Ternary.False;
        }
        if (sourcePropAccessibility != ModifierFlags.None)
        {
            if (GetTargetSymbolWorker(sourceProp) != GetTargetSymbolWorker(targetProp))
            {
                return Ternary.False;
            }
        }
        else
        {
            if ((sourceProp.Flags & SymbolFlags.Optional) != (targetProp.Flags & SymbolFlags.Optional))
            {
                return Ternary.False;
            }
        }
        if (IsReadonlySymbolWorker(sourceProp) != IsReadonlySymbolWorker(targetProp))
        {
            return Ternary.False;
        }
        return compareTypes(GetNonMissingTypeOfSymbolWorker(sourceProp), GetNonMissingTypeOfSymbolWorker(targetProp));
    }

    internal static Ternary CompareTypesEqual(Type s, Type t)
    {
        if (s == t)
        {
            return Ternary.True;
        }
        return Ternary.False;
    }

    internal void MarkPropertyAsReferenced(Symbol prop, Node? nodeForCheckWriteOnly, bool isSelfTypeAccess)
    {
        if ((prop.Flags & SymbolFlags.ClassMember) == 0 || prop.ValueDeclaration == null)
        {
            return;
        }
        var hasPrivateModifier = HasModifier(prop.ValueDeclaration, ModifierFlags.Private);
        var hasPrivateIdentifier = prop.ValueDeclaration.Name() != null && IsPrivateIdentifier(prop.ValueDeclaration.Name()!);
        if (!hasPrivateModifier && !hasPrivateIdentifier)
        {
            return;
        }
        if (nodeForCheckWriteOnly != null && IsWriteOnlyAccess(nodeForCheckWriteOnly) && (prop.Flags & SymbolFlags.SetAccessor) == 0)
        {
            return;
        }
        if (isSelfTypeAccess)
        {
            // Find any FunctionLikeDeclaration because those create a new 'this' binding. But this should only matter for methods (or getters/setters).
            var containingMethod = FindAncestor(nodeForCheckWriteOnly, n => IsFunctionLikeDeclaration(n));
            if (containingMethod != null && containingMethod.Symbol == prop)
            {
                return;
            }
        }
        var target = prop;
        if ((prop.CheckFlags & CheckFlags.Instantiated) != 0)
        {
            target = _valueSymbolLinks.Get(prop)._target!;
        }
        _symbolReferenceLinks.Get(target)._referenceKinds |= SymbolFlags.All;
    }

    internal Symbol[] ExpandSignatureParametersWithTupleMembers(Signature signature, TypeReference restType, int restIndex, Symbol? restSymbol)
    {
        var elementTypes = GetTypeArgumentsWorker(restType);
        var elementInfos = restType.TargetTupleType()!.ElementInfos();
        var associatedNames = GetUniqAssociatedNamesFromTupleType(restType, restSymbol);
        var expanded = new List<Symbol>(signature._parameters![..restIndex]);
        for (var i = 0; i < elementTypes.Length; i++)
        {
            var t = elementTypes[i];
            var flags = elementInfos[i]._flags;
            var checkFlags = CheckFlags.None;
            if ((flags & ElementFlags.Variable) != 0)
            {
                checkFlags = CheckFlags.RestParameter;
            }
            else if ((flags & ElementFlags.Optional) != 0)
            {
                checkFlags = CheckFlags.OptionalParameter;
            }
            var symbol = NewSymbolEx(SymbolFlags.FunctionScopedVariable, associatedNames[i], checkFlags);
            var links = _valueSymbolLinks.Get(symbol);
            if ((flags & ElementFlags.Rest) != 0)
            {
                links._resolvedType = CreateArrayType(t);
            }
            else
            {
                links._resolvedType = t;
            }
            expanded.Add(symbol);
        }
        return expanded.ToArray();
    }

    internal string[] GetUniqAssociatedNamesFromTupleType(TypeReference t, Symbol? restSymbol)
    {
        var elementInfos = t.TargetTupleType()!.ElementInfos();
        var names = new string[elementInfos.Length];
        var counters = new Dictionary<string, int>();
        for (var i = 0; i < elementInfos.Length; i++)
        {
            var info = elementInfos[i];
            names[i] = GetTupleElementLabel(info, restSymbol, i);
            // count duplicates using negative values
            counters[names[i]] = counters.GetValueOrDefault(names[i]) - 1;
        }
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            if (counters.GetValueOrDefault(name) == -1)
            {
                continue;
            }
            while (true)
            {
                if (counters.GetValueOrDefault(name) < 0)
                {
                    // switch to a positive suffix counter
                    counters[name] = 0;
                }
                counters[name]++;
                var candidateName = name + "_" + counters[name].ToString();
                if (counters.GetValueOrDefault(candidateName) == 0)
                {
                    names[i] = candidateName;
                    break;
                }
            }
        }
        return names;
    }

    internal static bool HasRestParameter(Node signature)
    {
        var last = LastOrNil(signature.Parameters());
        return last != null && IsRestParameter(last);
    }

    internal static bool IsRestParameter(Node param)
    {
        return param.AsParameterDeclaration()!.DotDotDotToken != null;
    }

    internal static string GetNameFromIndexInfo(IndexInfo info)
    {
        if (info._declaration != null)
        {
            return DeclarationNameToString(info._declaration.Parameters()![0].Name());
        }
        return "x";
    }

    internal bool IsUnknownLikeUnionType(Type t)
    {
        if (_strictNullChecks && (t._flags & TypeFlags.Union) != 0)
        {
            if ((t._objectFlags & ObjectFlags.IsUnknownLikeUnionComputed) == 0)
            {
                t._objectFlags |= ObjectFlags.IsUnknownLikeUnionComputed;
                var types = t.Types();
                if (types.Length >= 3 && (types[0]._flags & TypeFlags.Undefined) != 0 && (types[1]._flags & TypeFlags.Null) != 0 && Some(types, IsEmptyAnonymousObjectType))
                {
                    t._objectFlags |= ObjectFlags.IsUnknownLikeUnion;
                }
            }
            return (t._objectFlags & ObjectFlags.IsUnknownLikeUnion) != 0;
        }
        return false;
    }

    // Return true the given type is a primitive union type where no two literal type constituents are
    // comparable. Specifically, that means (a) the union doesn't contain literals from different enum
    // types, and (b) the union doesn't contain both enum literals and string or number literals.
    internal bool IsUniformUnionType(Type t)
    {
        if ((t._objectFlags & ObjectFlags.PrimitiveUnion) != 0)
        {
            if ((t._objectFlags & ObjectFlags.IsUniformEnumComputed) == 0)
            {
                t._objectFlags |= ObjectFlags.IsUniformEnumComputed | IfElse(ComputeIsUniformUnionType(t.Types()), ObjectFlags.IsUniformEnum, ObjectFlags.None);
            }
            return (t._objectFlags & ObjectFlags.IsUniformEnum) != 0;
        }
        return false;
    }

    internal bool ComputeIsUniformUnionType(Type[] types)
    {
        Symbol? enumSymbol = null;
        var hasStringOrNumberLiteral = false;
        foreach (var t in types)
        {
            if ((t._flags & TypeFlags.EnumLike) != 0)
            {
                if (hasStringOrNumberLiteral)
                {
                    return false;
                }
                var parent = GetParentOfSymbol(t._symbol!);
                if (enumSymbol == null)
                {
                    enumSymbol = parent;
                }
                else if (enumSymbol != parent)
                {
                    return false;
                }
            }
            else if ((t._flags & TypeFlags.StringOrNumberLiteral) != 0)
            {
                if (enumSymbol != null)
                {
                    return false;
                }
                hasStringOrNumberLiteral = true;
            }
        }
        return true;
    }

    internal bool ContainsUndefinedType(Type t)
    {
        if ((t._flags & TypeFlags.Union) != 0)
        {
            t = t.Types()[0];
        }
        return (t._flags & TypeFlags.Undefined) != 0;
    }

    internal bool TypeHasCallOrConstructSignaturesWorker(Type t)
    {
        // Go checker.go:28266 用 `len(...signatures) != 0`（nil slice 长度为 0）→ C# 不能直接 .Length，用 ?.Length ?? 0。
        return (t._flags & TypeFlags.StructuredType) != 0 && (ResolveStructuredTypeMembers(t)._signatures?.Length ?? 0) != 0;
    }

    internal Type GetNormalizedType(Type t, bool writing)
    {
        while (true)
        {
            Type n;
            if (IsFreshLiteralType(t))
            {
                n = t.AsLiteralType()!._regularType!;
            }
            else if (IsGenericTupleType(t))
            {
                n = GetNormalizedTupleType(t, writing);
            }
            else if ((t._objectFlags & ObjectFlags.Reference) != 0)
            {
                if (t.AsTypeReference()!._node != null)
                {
                    // Go checker.go:28274 对 TypeReference 直接调 t.Target()（Go 的 Target() 在无 target 时 panic）⇒ 必非 nil。
                    n = CreateTypeReference(t.Target()!, GetTypeArgumentsWorker(t));
                }
                else
                {
                    var singleBase = GetSingleBaseForNonAugmentingSubtype(t);
                    n = singleBase != null ? singleBase : t;
                }
            }
            else if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
            {
                n = GetNormalizedUnionOrIntersectionType(t, writing);
            }
            else if ((t._flags & TypeFlags.Substitution) != 0)
            {
                if (writing)
                {
                    n = t.AsSubstitutionType()!._baseType!;
                }
                else
                {
                    n = GetSubstitutionIntersection(t);
                }
            }
            else if ((t._flags & TypeFlags.Simplifiable) != 0)
            {
                n = GetSimplifiedType(t, writing);
            }
            else
            {
                return t;
            }
            if (n == t)
            {
                return n;
            }
            t = n;
        }
    }

    internal Type GetSimplifiedType(Type t, bool writing)
    {
        if ((t._flags & TypeFlags.IndexedAccess) != 0)
        {
            return GetSimplifiedIndexedAccessType(t, writing);
        }
        if ((t._flags & TypeFlags.Conditional) != 0)
        {
            return GetSimplifiedConditionalType(t, writing);
        }
        return t;
    }

    // Transform an indexed access to a simpler form, if possible. Return the simpler form, or return
    // the type itself if no transformation is possible. The writing flag indicates that the type is
    // the target of an assignment.
    internal Type GetSimplifiedIndexedAccessType(Type t, bool writing)
    {
        var key = new CachedTypeKey(IfElse(writing, CachedTypeKind.IndexedAccessForWriting, CachedTypeKind.IndexedAccessForReading), t._id);
        if (_cachedTypes.TryGetValue(key, out var cached) && cached is not null)
        {
            return IfElse(cached == _circularConstraintType, t, cached);
        }
        _cachedTypes[key] = t;
        var result = GetSimplifiedIndexedAccessTypeWorker(t, writing);
        if (result != t)
        {
            // If the simplification is a union type that includes t, remove t from the type.
            result = RemoveType(result, t);
            _cachedTypes[key] = result;
        }
        return result;
    }

    internal Type GetSimplifiedIndexedAccessTypeWorker(Type t, bool writing)
    {
        // We recursively simplify the object type as it may in turn be an indexed access type. For example, with
        // '{ [P in T]: { [Q in U]: number } }[T][U]' we want to first simplify the inner indexed access type.
        var objectType = GetSimplifiedType(t.AsIndexedAccessType()!._objectType!, writing);
        var indexType = GetSimplifiedType(t.AsIndexedAccessType()!._indexType!, writing);
        // T[A | B] -> T[A] | T[B] (reading)
        // T[A | B] -> T[A] & T[B] (writing)
        var distributedOverIndex = DistributeObjectOverIndexType(objectType, indexType, writing);
        if (distributedOverIndex != null)
        {
            return distributedOverIndex;
        }
        // Only do the inner distributions if the index can no longer be instantiated to cause index distribution again
        if ((indexType._flags & TypeFlags.Instantiable) == 0)
        {
            // (T | U)[K] -> T[K] | U[K] (reading)
            // (T | U)[K] -> T[K] & U[K] (writing)
            // (T & U)[K] -> T[K] & U[K]
            var distributedOverObject = DistributeIndexOverObjectType(objectType, indexType, writing);
            if (distributedOverObject != null)
            {
                return distributedOverObject;
            }
        }
        // So ultimately (reading):
        // ((A & B) | C)[K1 | K2] -> ((A & B) | C)[K1] | ((A & B) | C)[K2] -> (A & B)[K1] | C[K1] | (A & B)[K2] | C[K2] -> (A[K1] & B[K1]) | C[K1] | (A[K2] & B[K2]) | C[K2]
        // A generic tuple type indexed by a number exists only when the index type doesn't select a
        // fixed element. We simplify to either the combined type of all elements (when the index type
        // the actual number type) or to the combined type of all non-fixed elements.
        if (IsGenericTupleType(objectType) && (indexType._flags & TypeFlags.NumberLike) != 0)
        {
            var elementType = GetElementTypeOfSliceOfTupleType(objectType, IfElse((indexType._flags & TypeFlags.Number) != 0, 0, objectType.TargetTupleType()!.FixedLength()), 0 /*endSkipCount*/, writing, false);
            if (elementType != null)
            {
                return elementType;
            }
        }
        // If the object type is a mapped type { [P in K]: E }, where K is generic, or { [P in K as N]: E }, where
        // K is generic and N is assignable to P, instantiate E using a mapper that substitutes the index type for P.
        // For example, for an index access { [P in K]: Box<T[P]> }[X], we construct the type Box<T[X]>.
        if (IsGenericMappedType(objectType))
        {
            if (GetMappedTypeNameTypeKind(objectType) != MappedTypeNameTypeKind.Remapping)
            {
                return MapType(SubstituteIndexedMappedType(objectType, t.AsIndexedAccessType()!._indexType!), u => GetSimplifiedType(u, writing));
            }
        }
        return t;
    }

    internal Type? DistributeObjectOverIndexType(Type objectType, Type indexType, bool writing)
    {
        // T[A | B] -> T[A] | T[B] (reading)
        // T[A | B] -> T[A] & T[B] (writing)
        if ((indexType._flags & TypeFlags.Union) != 0)
        {
            var types = Map(indexType.Types(), t => GetSimplifiedType(GetIndexedAccessType(objectType, t), writing));
            if (writing)
            {
                return GetIntersectionType(types);
            }
            return GetUnionTypeWorker(types);
        }
        return null;
    }

    internal Type? DistributeIndexOverObjectType(Type objectType, Type indexType, bool writing)
    {
        // (T | U)[K] -> T[K] | U[K] (reading)
        // (T | U)[K] -> T[K] & U[K] (writing)
        // (T & U)[K] -> T[K] & U[K]
        if ((objectType._flags & TypeFlags.Union) != 0 || (objectType._flags & TypeFlags.Intersection) != 0 && !ShouldDeferIndexType(objectType, IndexFlags.None))
        {
            var types = Map(objectType.Types(), t => GetSimplifiedType(GetIndexedAccessType(t, indexType), writing));
            if ((objectType._flags & TypeFlags.Intersection) != 0 || writing)
            {
                return GetIntersectionType(types);
            }
            return GetUnionTypeWorker(types);
        }
        return null;
    }

    internal Type GetSimplifiedConditionalType(Type t, bool writing)
    {
        var checkType = t.AsConditionalType()!._checkType!;
        var extendsType = t.AsConditionalType()!._extendsType!;
        var trueType = GetTrueTypeFromConditionalType(t)!;
        var falseType = GetFalseTypeFromConditionalType(t)!;
        // Simplifications for types of the form `T extends U ? T : never` and `T extends U ? never : T`.
        if ((falseType._flags & TypeFlags.Never) != 0 && GetActualTypeVariable(trueType) == GetActualTypeVariable(checkType))
        {
            if ((checkType._flags & TypeFlags.Any) != 0 || IsTypeAssignableToWorker(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(extendsType)))
            {
                return GetSimplifiedType(trueType, writing);
            }
            else if (IsIntersectionEmpty(checkType, extendsType))
            {
                return _neverType;
            }
        }
        else if ((trueType._flags & TypeFlags.Never) != 0 && GetActualTypeVariable(falseType) == GetActualTypeVariable(checkType))
        {
            if ((checkType._flags & TypeFlags.Any) == 0 && IsTypeAssignableToWorker(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(extendsType)))
            {
                return _neverType;
            }
            else if ((checkType._flags & TypeFlags.Any) != 0 || IsIntersectionEmpty(checkType, extendsType))
            {
                return GetSimplifiedType(falseType, writing);
            }
        }
        return t;
    }

    // Invokes union simplification logic to determine if an intersection is considered empty as a union constituent
    internal bool IsIntersectionEmpty(Type type1, Type type2)
    {
        return (GetUnionTypeWorker(new[] { IntersectTypes(type1, type2), _neverType })._flags & TypeFlags.Never) != 0;
    }

    internal Type? GetSimplifiedTypeOrConstraint(Type t)
    {
        var simplified = GetSimplifiedType(t, false /*writing*/);
        if (simplified != t)
        {
            return simplified;
        }
        return GetConstraintOfType(t);
    }

    internal Type GetNormalizedUnionOrIntersectionType(Type t, bool writing)
    {
        var reduced = GetReducedTypeWorker(t);
        if (reduced != t)
        {
            return reduced;
        }
        if ((t._flags & TypeFlags.Intersection) != 0 && ShouldNormalizeIntersection(t))
        {
            // Normalization handles cases like
            // Partial<T>[K] & ({} | null) ==>
            // Partial<T>[K] & {} | Partial<T>[K} & null ==>
            // (T[K] | undefined) & {} | (T[K] | undefined) & null ==>
            // T[K] & {} | undefined & {} | T[K] & null | undefined & null ==>
            // T[K] & {} | T[K] & null
            var types = t.Types();
            var normalizedTypes = SameMap(types, u => GetNormalizedType(u, writing));
            if (!Same(normalizedTypes, types))
            {
                return GetIntersectionType(normalizedTypes);
            }
        }
        return t;
    }

    internal bool ShouldNormalizeIntersection(Type t)
    {
        var hasInstantiable = false;
        var hasNullableOrEmpty = false;
        foreach (var u in t.Types())
        {
            hasInstantiable = hasInstantiable || (u._flags & TypeFlags.Instantiable) != 0;
            hasNullableOrEmpty = hasNullableOrEmpty || (u._flags & TypeFlags.Nullable) != 0 || IsEmptyAnonymousObjectType(u);
            if (hasInstantiable && hasNullableOrEmpty)
            {
                return true;
            }
        }
        return false;
    }

    internal Type GetNormalizedTupleType(Type t, bool writing)
    {
        var elements = GetElementTypes(t);
        var normalizedElements = SameMap(elements, u =>
        {
            if ((u._flags & TypeFlags.Simplifiable) != 0)
            {
                return GetSimplifiedType(u, writing);
            }
            return u;
        });
        if (!Same(elements, normalizedElements))
        {
            // Go checker.go:28486 对 tuple type 直接调 t.Target()（Go 的 Target() 无 target 时 panic）⇒ 必非 nil。
            return CreateNormalizedTupleType(t.Target()!, normalizedElements);
        }
        return t;
    }
}
