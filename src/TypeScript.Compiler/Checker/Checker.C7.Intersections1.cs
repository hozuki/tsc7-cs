// ported from: internal/checker/checker.go:26285-26661（联合成员剪枝 + 交集类型构造）

namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type[] RemoveConstrainedTypeVariables(Type[]? types)
    {
        types ??= [];
        Type[] typeVariables = [];
        // First collect a list of the type variables occurring in constraining intersections.
        foreach (var t in types)
        {
            if ((t._flags & TypeFlags.Intersection) != 0 && (t._objectFlags & ObjectFlags.IsConstrainedTypeVariable) != 0)
            {
                var index = 0;
                if ((t.AsIntersectionType()!._types![0]._flags & TypeFlags.TypeVariable) == 0)
                {
                    index = 1;
                }
                typeVariables = AppendIfUnique(typeVariables, t.AsIntersectionType()!._types![index]);
            }
        }
        // For each type variable, check if the constraining intersections for that type variable fully
        // cover the constraint of the type variable; if so, remove the constraining intersections and
        // substitute the type variable.
        foreach (var typeVariable in typeVariables)
        {
            Type[] primitives = [];
            // First collect the primitive types from the constraining intersections.
            foreach (var t in types)
            {
                if ((t._flags & TypeFlags.Intersection) != 0 && (t._objectFlags & ObjectFlags.IsConstrainedTypeVariable) != 0)
                {
                    var index = 0;
                    if ((t.AsIntersectionType()!._types![0]._flags & TypeFlags.TypeVariable) == 0)
                    {
                        index = 1;
                    }
                    if (t.AsIntersectionType()!._types![index] == typeVariable)
                    {
                        TryInsertType(primitives, t.AsIntersectionType()!._types![1 - index], out primitives);
                    }
                }
            }
            // If every constituent in the type variable's constraint is covered by an intersection of the type
            // variable and that constituent, remove those intersections and substitute the type variable.
            var constraint = GetBaseConstraintOfTypeWorker(typeVariable);
            if (EveryType(constraint!, t => ContainsType(primitives, t)))
            {
                var i = types.Length;
                while (i > 0)
                {
                    i--;
                    var t = types[i];
                    if ((t._flags & TypeFlags.Intersection) != 0 && (t._objectFlags & ObjectFlags.IsConstrainedTypeVariable) != 0)
                    {
                        var index = 0;
                        if ((t.AsIntersectionType()!._types![0]._flags & TypeFlags.TypeVariable) == 0)
                        {
                            index = 1;
                        }
                        if (t.AsIntersectionType()!._types![index] == typeVariable && ContainsType(primitives, t.AsIntersectionType()!._types![1 - index]))
                        {
                            types = Splice(types, i, 1);
                        }
                    }
                }
                TryInsertType(types, typeVariable, out types);
            }
        }
        return types;
    }

    internal Type[]? RemoveSubtypes(Type[] types, bool hasObjectTypes)
    {
        // [] and [T] immediately reduce to [] and [T] respectively
        if (types.Length < 2)
        {
            return types;
        }
        var key = GetTypeListKey(types);
        if (_subtypeReductionCache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }
        // We assume that redundant primitive types have already been removed from the types array and that there
        // are no any and unknown types in the array. Thus, the only possible supertypes for primitive types are empty
        // object types, and if none of those are present we can exclude primitive types from the subtype check.
        var hasEmptyObject = hasObjectTypes && Some(types, t =>
            (t._flags & TypeFlags.Object) != 0 && !IsGenericMappedType(t) && IsEmptyResolvedType(ResolveStructuredTypeMembers(t)));
        var length = types.Length;
        var i = length;
        var count = 0;
        while (i > 0)
        {
            i--;
            var source = types[i];
            if (hasEmptyObject || (source._flags & TypeFlags.StructuredOrInstantiable) != 0)
            {
                // A type parameter with a union constraint may be a subtype of some union, but not a subtype of the
                // individual constituents of that union. For example, `T extends A | B` is a subtype of `A | B`, but not
                // a subtype of just `A` or just `B`. When we encounter such a type parameter, we therefore check if the
                // type parameter is a subtype of a union of all the other types.
                if ((source._flags & TypeFlags.TypeParameter) != 0 && (GetBaseConstraintOrType(source)._flags & TypeFlags.Union) != 0)
                {
                    if (IsTypeRelatedTo(source, GetUnionTypeWorker(Map(types, t =>
                    {
                        if (t == source)
                        {
                            return _neverType!;
                        }
                        return t;
                    })), _strictSubtypeRelation))
                    {
                        types = Splice(types, i, 1);
                    }
                    continue;
                }
                // Find the first property with a unit type, if any. When constituents have a property by the same name
                // but of a different unit type, we can quickly disqualify them from subtype checks. This helps subtype
                // reduction of large discriminated union types.
                Symbol? keyProperty = null;
                Type? keyPropertyType = null;
                if ((source._flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0)
                {
                    keyProperty = Find(GetPropertiesOfTypeWorker(source), p => IsUnitType(GetTypeOfSymbolWorker(p)));
                }
                if (keyProperty != null)
                {
                    keyPropertyType = GetRegularTypeOfLiteralType(GetTypeOfSymbolWorker(keyProperty));
                }
                foreach (var target in types)
                {
                    if (source != target)
                    {
                        if (count == 100000)
                        {
                            // After 100000 subtype checks we estimate the remaining amount of work by assuming the
                            // same ratio of checks per element. If the estimated number of remaining type checks is
                            // greater than 1M we deem the union type too complex to represent. This for example
                            // caps union types at 1000 unique object types.
                            var estimatedCount = (count / (length - i)) * length;
                            if (estimatedCount > 1000000)
                            {
                                var tr = _tracer;
                                if (tr != null)
                                {
                                    tr.Instant(Phase.CheckTypes, "removeSubtypes_DepthLimit", new Dictionary<string, object?> { ["estimatedCount"] = estimatedCount });
                                }
                                Error(_currentNode, Diagnostics.Expression_produces_a_union_type_that_is_too_complex_to_represent);
                                return null;
                            }
                        }
                        count++;
                        if (keyProperty != null && (target._flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0)
                        {
                            var t = GetTypeOfPropertyOfTypeWorker(target, keyProperty.Name);
                            if (t != null && IsUnitType(t) && GetRegularTypeOfLiteralType(t) != keyPropertyType)
                            {
                                continue;
                            }
                        }
                        if ((source == _emptyObjectType || source == _unknownEmptyObjectType) && target._symbol != null && IsEmptyAnonymousObjectType(target))
                        {
                            continue;
                        }
                        if (IsTypeRelatedTo(source, target, _strictSubtypeRelation) && ((GetTargetType(source)._objectFlags & ObjectFlags.Class) == 0 ||
                            (GetTargetType(target)._objectFlags & ObjectFlags.Class) == 0 ||
                            IsTypeDerivedFrom(source, target)))
                        {
                            types = Splice(types, i, 1);
                            break;
                        }
                    }
                }
            }
        }
        _subtypeReductionCache[key] = types;
        return types;
    }

    internal Type IntersectTypes(Type? type1, Type? type2)
    {
        if (type1 == null)
        {
            return type2!;
        }
        if (type2 == null)
        {
            return type1!;
        }
        return GetIntersectionType([type1, type2]);
    }

    // We normalize combinations of intersection and union types based on the distributive property of the '&'
    // operator. Specifically, because X & (A | B) is equivalent to X & A | X & B, we can transform intersection
    // types with union type constituents into equivalent union types with intersection type constituents and
    // effectively ensure that union types are always at the top level in type representations.
    //
    // We do not perform structural deduplication on intersection types. Intersection types are created only by the &
    // type operator and we can't reduce those because we want to support recursive intersection types. For example,
    // a type alias of the form "type List<T> = T & { next: List<T> }" cannot be reduced during its declaration.
    // Also, unlike union types, the order of the constituent types is preserved in order that overload resolution
    // for intersections of types with signatures can be deterministic.
    internal Type GetIntersectionType(Type[] types)
    {
        return GetIntersectionTypeEx(types, IntersectionFlags.None, null /*alias*/);
    }

    internal Type GetIntersectionTypeEx(Type[] types, IntersectionFlags flags, TypeAlias? alias)
    {
        var orderedTypes = new SmallOrderedSet<Type>();
        orderedTypes._values = new List<Type>(types.Length);
        var includes = AddTypesToIntersection(orderedTypes, 0, types);
        var typeSet = orderedTypes._values.ToArray();
        var objectFlags = ObjectFlags.None;
        // An intersection type is considered empty if it contains
        // the type never, or
        // more than one unit type or,
        // an object type and a nullable type (null or undefined), or
        // a string-like type and a type known to be non-string-like, or
        // a number-like type and a type known to be non-number-like, or
        // a symbol-like type and a type known to be non-symbol-like, or
        // a void-like type and a type known to be non-void-like, or
        // a non-primitive type and a type known to be primitive.
        if ((includes & TypeFlags.Never) != 0)
        {
            if (Array.IndexOf(typeSet, _silentNeverType) >= 0)
            {
                return _silentNeverType!;
            }
            return _neverType!;
        }
        if ((_strictNullChecks && (includes & TypeFlags.Nullable) != 0 && (includes & (TypeFlags.Object | TypeFlags.NonPrimitive | TypeFlags.IncludesEmptyObject)) != 0) ||
            ((includes & TypeFlags.NonPrimitive) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.NonPrimitive)) != 0) ||
            ((includes & TypeFlags.StringLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.StringLike)) != 0) ||
            ((includes & TypeFlags.NumberLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.NumberLike)) != 0) ||
            ((includes & TypeFlags.BigIntLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.BigIntLike)) != 0) ||
            ((includes & TypeFlags.ESSymbolLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.ESSymbolLike)) != 0) ||
            ((includes & TypeFlags.VoidLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.VoidLike)) != 0))
        {
            return _neverType!;
        }
        if ((includes & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && (includes & TypeFlags.StringLiteral) != 0)
        {
            var isEmptySet = TryExtractRedundantTemplateLiterals(typeSet, out typeSet);
            if (isEmptySet)
            {
                return _neverType!;
            }
        }
        if ((includes & TypeFlags.Any) != 0)
        {
            if ((includes & TypeFlags.IncludesWildcard) != 0)
            {
                return _wildcardType!;
            }
            if ((includes & TypeFlags.IncludesError) != 0)
            {
                return _errorType!;
            }
            return _anyType!;
        }
        if (!_strictNullChecks && (includes & TypeFlags.Nullable) != 0)
        {
            if ((includes & TypeFlags.IncludesEmptyObject) != 0)
            {
                return _neverType!;
            }
            if ((includes & TypeFlags.Undefined) != 0)
            {
                return _undefinedType!;
            }
            return _nullType!;
        }
        if (((includes & TypeFlags.String) != 0 && (includes & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0) ||
            ((includes & TypeFlags.Number) != 0 && (includes & TypeFlags.NumberLiteral) != 0) ||
            ((includes & TypeFlags.BigInt) != 0 && (includes & TypeFlags.BigIntLiteral) != 0) ||
            ((includes & TypeFlags.ESSymbol) != 0 && (includes & TypeFlags.UniqueESSymbol) != 0) ||
            ((includes & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0) ||
            ((includes & TypeFlags.IncludesEmptyObject) != 0 && (includes & TypeFlags.DefinitelyNonNullable) != 0))
        {
            if ((flags & IntersectionFlags.NoSupertypeReduction) == 0)
            {
                typeSet = RemoveRedundantSupertypes(typeSet, includes);
            }
        }
        if ((includes & TypeFlags.IncludesMissingType) != 0)
        {
            typeSet[Array.IndexOf(typeSet, _undefinedType)] = _missingType!;
        }
        if (typeSet.Length == 0)
        {
            return _unknownType!;
        }
        if (typeSet.Length == 1)
        {
            return typeSet[0];
        }
        if (typeSet.Length == 2 && (flags & IntersectionFlags.NoConstraintReduction) == 0)
        {
            var typeVarIndex = 0;
            if ((typeSet[0]._flags & TypeFlags.TypeVariable) == 0)
            {
                typeVarIndex = 1;
            }
            var typeVariable = typeSet[typeVarIndex];
            var primitiveType = typeSet[1 - typeVarIndex];
            if ((typeVariable._flags & TypeFlags.TypeVariable) != 0 && (((primitiveType._flags & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 && !IsGenericStringLikeType(primitiveType)) ||
                (includes & TypeFlags.IncludesEmptyObject) != 0))
            {
                // We have an intersection T & P or P & T, where T is a type variable and P is a primitive type, the object type, or {}.
                var constraint = GetBaseConstraintOfTypeWorker(typeVariable);
                // Check that T's constraint is similarly composed of primitive types, the object type, or {}.
                if (constraint != null && EveryType(constraint, _isPrimitiveOrObjectOrEmptyType))
                {
                    // If T's constraint is a subtype of P, simply return T. For example, given `T extends "a" | "b"`,
                    // the intersection `T & string` reduces to just T.
                    if (IsTypeStrictSubtypeOf(constraint, primitiveType))
                    {
                        return typeVariable;
                    }
                    if (!((constraint._flags & TypeFlags.Union) != 0 && SomeType(constraint, n => IsTypeStrictSubtypeOf(n, primitiveType))))
                    {
                        // No constituent of T's constraint is a subtype of P. If P is also not a subtype of T's constraint,
                        // then the constraint and P are unrelated, and the intersection reduces to never. For example, given
                        // `T extends "a" | "b"`, the intersection `T & number` reduces to never.
                        if (!IsTypeStrictSubtypeOf(primitiveType, constraint))
                        {
                            return _neverType!;
                        }
                    }
                    // Some constituent of T's constraint is a subtype of P, or P is a subtype of T's constraint. Thus,
                    // the intersection further constrains the type variable. For example, given `T extends string | number`,
                    // the intersection `T & "a"` is marked as a constrained type variable. Likewise, given `T extends "a" | 1`,
                    // the intersection `T & number` is marked as a constrained type variable.
                    objectFlags = ObjectFlags.IsConstrainedTypeVariable;
                }
            }
        }
        var key = GetIntersectionKey(typeSet, flags, alias);
        Type? result = _intersectionTypes.GetValueOrDefault(key);
        if (result == null)
        {
            if ((includes & TypeFlags.Union) != 0)
            {
                var reduced = TryIntersectUnionsOfPrimitiveTypes(typeSet, out typeSet);
                if (reduced)
                {
                    // When the intersection creates a reduced set (which might mean that *all* union types have
                    // disappeared), we restart the operation to get a new set of combined flags. Once we have
                    // reduced we'll never reduce again, so this occurs at most once.
                    result = GetIntersectionTypeEx(typeSet, flags, alias);
                }
                else if (Every(typeSet, IsUnionWithUndefined))
                {
                    var containedUndefinedType = _undefinedType!;
                    if (Some(typeSet, _containsMissingType))
                    {
                        containedUndefinedType = _missingType!;
                    }
                    FilterTypes(typeSet, IsNotUndefinedType);
                    result = GetUnionTypeExWorker([GetIntersectionTypeEx(typeSet, flags, null /*alias*/), containedUndefinedType], UnionReduction.Literal, alias, null /*origin*/);
                }
                else if (Every(typeSet, IsUnionWithNull))
                {
                    FilterTypes(typeSet, IsNotNullType);
                    result = GetUnionTypeExWorker([GetIntersectionTypeEx(typeSet, flags, null /*alias*/), _nullType!], UnionReduction.Literal, alias, null /*origin*/);
                }
                else if (typeSet.Length >= 3 && types.Length > 2)
                {
                    // When we have three or more constituents, more than two inputs (to head off infinite reexpansion), some of which are unions, we employ a "divide and conquer" strategy
                    // where A & B & C & D is processed as (A & B) & (C & D). Since intersections of unions often produce far smaller
                    // unions of intersections than the full cartesian product (due to some intersections becoming `never`), this can
                    // dramatically reduce the overall work.
                    var middle = typeSet.Length / 2;
                    result = GetIntersectionTypeEx([
                        GetIntersectionTypeEx(typeSet[..middle], flags, null /*alias*/),
                        GetIntersectionTypeEx(typeSet[middle..], flags, null /*alias*/),
                    ], flags, alias);
                }
                else
                {
                    // We are attempting to construct a type of the form X & (A | B) & (C | D). Transform this into a type of
                    // the form X & A & C | X & A & D | X & B & C | X & B & D. If the estimated size of the resulting union type
                    // exceeds 100000 constituents, report an error.
                    if (!CheckCrossProductUnion(typeSet))
                    {
                        return _errorType!;
                    }
                    var constituents = GetCrossProductIntersections(typeSet, flags);
                    // We attach a denormalized origin type when at least one constituent of the cross-product union is an
                    // intersection (i.e. when the intersection didn't just reduce one or more unions to smaller unions) and
                    // the denormalized origin has fewer constituents than the union itself.
                    Type? origin = null;
                    if (Some(constituents, IsIntersectionType) && GetConstituentCountOfTypes(constituents) > GetConstituentCountOfTypes(typeSet))
                    {
                        origin = NewIntersectionType(ObjectFlags.None, typeSet);
                    }
                    result = GetUnionTypeExWorker(constituents, UnionReduction.Literal, alias, origin);
                }
            }
            else
            {
                result = NewIntersectionType(objectFlags | GetPropagatingFlagsOfTypes(types /*excludeKinds*/, TypeFlags.Nullable), typeSet);
                result._alias = alias;
            }
            _intersectionTypes[key] = result!;
        }
        return result!;
    }

    internal static bool IsUnionWithUndefined(Type t) =>
        (t._flags & TypeFlags.Union) != 0 && (t.Types()[0]._flags & TypeFlags.Undefined) != 0;

    internal static bool IsUnionWithNull(Type t) =>
        (t._flags & TypeFlags.Union) != 0 && ((t.Types()[0]._flags & TypeFlags.Null) != 0 || (t.Types()[1]._flags & TypeFlags.Null) != 0);

    internal static bool IsIntersectionType(Type t) =>
        (t._flags & TypeFlags.Intersection) != 0;

    internal static bool IsPrimitiveUnion(Type t) =>
        (t._objectFlags & ObjectFlags.PrimitiveUnion) != 0;

    internal static bool IsNotUndefinedType(Type t) =>
        (t._flags & TypeFlags.Undefined) == 0;

    internal static bool IsNotNullType(Type t) =>
        (t._flags & TypeFlags.Null) == 0;

    // Add the given types to the given type set. Order is preserved, freshness is removed from literal
    // types, duplicates are removed, and nested types of the given kind are flattened into the set.
    internal TypeFlags AddTypesToIntersection(SmallOrderedSet<Type> typeSet, TypeFlags includes, Type[] types)
    {
        foreach (var t in types)
        {
            includes = AddTypeToIntersection(typeSet, includes, GetRegularTypeOfLiteralType(t));
        }
        return includes;
    }
}

[Flags]
public enum IntersectionFlags : uint
{
    None = 0,
    NoSupertypeReduction = 1u << 0,
    NoConstraintReduction = 1u << 1,
}
