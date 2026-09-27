// ported from: internal/checker/checker.go:25891-26284（字面量拓宽 + map 类型 + 联合类型构造）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Worker 改名（artifacts/checker-method-collisions.md）：Go 未导出的 `getWidenedLiteralType` → `GetWidenedLiteralTypeWorker`、
//    `getUnionType` → `GetUnionTypeWorker`、`getUnionTypeEx` → `GetUnionTypeExWorker`（三个导出版由 Exports.cs 定义，本文件不定义）。
// 2) **新冲突（本分片发现，已记入报告）**：Go 的 `getUnionTypeWorker`（checker.go:26057，未导出、冲突清单里没有对应导出名）
//    与上条改名后的 `GetUnionTypeWorker` 在 C# 里会同名 → 本文件取 `GetUnionTypeWorkerImpl`（定义点与本文件内调用点一致）。
// 3) `type UnionReduction int32` + 3 个 iota 常量 → 命名空间级 `public enum UnionReduction { None = 0, Literal = 1, Subtype = 2 }`。
//    必须 `public` 且不能嵌套在 Checker 里：`Exports.cs` 的 `public GetUnionTypeEx(Type[], UnionReduction)` 用到它（internal 会 CS0051），
//    `Checker.Keys.cs` 的 `UnionOfUnionKey`（在 Checker 类外）也直接引用它。
// 4) `UnionTypes() iter.Seq[*Type]` → `IEnumerable<Type>`（Go 调用点是 `for union := range c.UnionTypes()`）。
// 5) `slices.Delete(types, i, i+1)` → `Splice(types, i, 1)`；`slices.Clone(xs)` → `xs.ToArray()`；
//    `slices.SortStableFunc(types, CompareTypes)` → `Array.Sort(types, (a, b) => CompareTypes(a, b))`
//    （CompareTypes 以唯一 type id 收尾，不存在"比较相等但不同一"的元素，稳定性不可观察；lambda 同时避开可空性告警）。
// 6) Go 的 `mapType` 系列能返回 nil；已落地的调用方（C4/C5/C8 的 `return MapType(...)`）要求非空 → `MapType`/`MapTypeWithAlias`
//    返回 `Type`，底层 `MapTypeEx` 保留 `Type?`（Go 的 nil 语义）。
// 7) `addNamedUnions` 在 Go 里可返回 nil 切片 → 本 C# 版返回空数组（调用点只做 Length/range，等价）。
// 8) `core.X`/`ast.X` 一律裸名（`Some`/`Map`/`Filter`/`IfElse`/`AppendIfUnique`/`Splice`/`CompareTypes`/`ContainsType`/`TryInsertType` …）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using static TypeScript.Core.Core;

/// <summary>Go: <c>type UnionReduction int32</c>（checker.go:26006）。</summary>
public enum UnionReduction
{
    /// <summary>Go: <c>UnionReductionNone</c>（iota = 0）。</summary>
    None = 0,

    /// <summary>Go: <c>UnionReductionLiteral</c>（iota = 1）。</summary>
    Literal = 1,

    /// <summary>Go: <c>UnionReductionSubtype</c>（iota = 2）。</summary>
    Subtype = 2,
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) getWidenedLiteralType(t *Type) *Type</c>（checker.go:25891-25907）。</summary>
    internal Type GetWidenedLiteralTypeWorker(Type t)
    {
        if ((t._flags & TypeFlags.EnumLike) != 0 && IsFreshLiteralType(t))
        {
            return GetBaseTypeOfEnumLikeType(t);
        }

        if ((t._flags & TypeFlags.StringLiteral) != 0 && IsFreshLiteralType(t))
        {
            return _stringType;
        }

        if ((t._flags & TypeFlags.NumberLiteral) != 0 && IsFreshLiteralType(t))
        {
            return _numberType;
        }

        if ((t._flags & TypeFlags.BigIntLiteral) != 0 && IsFreshLiteralType(t))
        {
            return _bigintType;
        }

        if ((t._flags & TypeFlags.BooleanLiteral) != 0 && IsFreshLiteralType(t))
        {
            return _booleanType;
        }

        if ((t._flags & TypeFlags.Union) != 0)
        {
            return MapType(t, GetWidenedLiteralTypeWorker);
        }

        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getWidenedUniqueESSymbolType(t *Type) *Type</c>（checker.go:25909-25917）。</summary>
    internal Type GetWidenedUniqueESSymbolType(Type t)
    {
        if ((t._flags & TypeFlags.UniqueESSymbol) != 0)
        {
            return _esSymbolType;
        }

        if ((t._flags & TypeFlags.Union) != 0)
        {
            return MapType(t, GetWidenedUniqueESSymbolType);
        }

        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getWidenedLiteralLikeTypeForContextualType(t *Type, contextualType *Type) *Type</c>（checker.go:25919-25924）。</summary>
    internal Type GetWidenedLiteralLikeTypeForContextualType(Type t, Type? contextualType)
    {
        if (!IsLiteralOfContextualType(t, contextualType))
        {
            t = GetWidenedUniqueESSymbolType(GetWidenedLiteralTypeWorker(t));
        }

        return GetRegularTypeOfLiteralType(t);
    }

    /// <summary>Go: <c>func (c *Checker) isLiteralOfContextualType(candidateType *Type, contextualType *Type) bool</c>（checker.go:25926-25956）。</summary>
    internal bool IsLiteralOfContextualType(Type candidateType, Type? contextualType)
    {
        if (contextualType != null)
        {
            if ((contextualType._flags & TypeFlags.UnionOrIntersection) != 0)
            {
                return Some(contextualType.Types(), t => IsLiteralOfContextualType(candidateType, t));
            }

            if ((contextualType._flags & TypeFlags.InstantiableNonPrimitive) != 0)
            {
                // If the contextual type is a type variable constrained to a primitive type, consider
                // this a literal context for literals of that primitive type. For example, given a
                // type parameter 'T extends string', infer string literal types for T.
                var constraint = GetBaseConstraintOfTypeWorker(contextualType);
                if (constraint == null)
                {
                    constraint = _unknownType;
                }

                return MaybeTypeOfKind(constraint, TypeFlags.String) && MaybeTypeOfKind(candidateType, TypeFlags.StringLiteral) ||
                    MaybeTypeOfKind(constraint, TypeFlags.Number) && MaybeTypeOfKind(candidateType, TypeFlags.NumberLiteral) ||
                    MaybeTypeOfKind(constraint, TypeFlags.BigInt) && MaybeTypeOfKind(candidateType, TypeFlags.BigIntLiteral) ||
                    MaybeTypeOfKind(constraint, TypeFlags.ESSymbol) && MaybeTypeOfKind(candidateType, TypeFlags.UniqueESSymbol) ||
                    IsLiteralOfContextualType(candidateType, constraint);
            }

            // If the contextual type is a literal of a particular primitive type, we consider this a
            // literal context for all literals of that primitive type.
            return (contextualType._flags & (TypeFlags.StringLiteral | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.StringLiteral) ||
                (contextualType._flags & TypeFlags.NumberLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.NumberLiteral) ||
                (contextualType._flags & TypeFlags.BigIntLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.BigIntLiteral) ||
                (contextualType._flags & TypeFlags.BooleanLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.BooleanLiteral) ||
                (contextualType._flags & TypeFlags.UniqueESSymbol) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.UniqueESSymbol);
        }

        return false;
    }

    /// <summary>Go: <c>func (c *Checker) mapTypeWithAlias(t *Type, f func(t *Type) *Type, alias *TypeAlias) *Type</c>（checker.go:25958-25963）。</summary>
    internal Type MapTypeWithAlias(Type t, Func<Type, Type> f, TypeAlias? alias)
    {
        if ((t._flags & TypeFlags.Union) != 0 && alias != null)
        {
            return GetUnionTypeExWorker(Map(t.Types(), f), UnionReduction.Literal, alias, null);
        }

        return MapType(t, f);
    }

    /// <summary>Go: <c>func (c *Checker) mapType(t *Type, f func(*Type) *Type) *Type</c>（checker.go:25965-25967）。</summary>
    internal Type MapType(Type t, Func<Type, Type> f)
    {
        return MapTypeEx(t, f, false /*noReductions*/)!;
    }

    /// <summary>Go: <c>func (c *Checker) mapTypeEx(t *Type, f func(*Type) *Type, noReductions bool) *Type</c>（checker.go:25969-26004）。</summary>
    internal Type? MapTypeEx(Type t, Func<Type, Type> f, bool noReductions)
    {
        if ((t._flags & TypeFlags.Never) != 0)
        {
            return t;
        }

        if ((t._flags & TypeFlags.Union) == 0)
        {
            return f(t);
        }

        var u = t.AsUnionType()!;
        var types = u.Types();
        if (u._origin != null && (u._origin._flags & TypeFlags.Union) != 0)
        {
            types = u._origin.Types();
        }

        var mappedTypes = new List<Type>(16);
        var changed = false;
        foreach (var s in types)
        {
            Type? mapped;
            if ((s._flags & TypeFlags.Union) != 0)
            {
                mapped = MapTypeEx(s, f, noReductions);
            }
            else
            {
                mapped = f(s);
            }

            if (mapped != s)
            {
                changed = true;
            }

            if (mapped != null)
            {
                mappedTypes.Add(mapped);
            }
        }

        if (changed)
        {
            if (mappedTypes.Count == 0)
            {
                return null;
            }

            return GetUnionTypeExWorker(mappedTypes.ToArray(), IfElse(noReductions, UnionReduction.None, UnionReduction.Literal), null /*alias*/, null /*origin*/);
        }

        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getUnionOrIntersectionType(types []*Type, isUnion bool, unionReduction UnionReduction) *Type</c>（checker.go:26014-26019）。</summary>
    internal Type GetUnionOrIntersectionType(Type[] types, bool isUnion, UnionReduction unionReduction)
    {
        if (isUnion)
        {
            return GetUnionTypeExWorker(types, unionReduction, null, null);
        }

        return GetIntersectionType(types);
    }

    /// <summary>Go: <c>func (c *Checker) getUnionType(types []*Type) *Type</c>（checker.go:26021-26023）。</summary>
    internal Type GetUnionTypeWorker(Type[] types)
    {
        return GetUnionTypeExWorker(types, UnionReduction.Literal, null /*alias*/, null /*origin*/);
    }

    // We sort and deduplicate the constituent types based on object identity. If the subtypeReduction
    // flag is specified we also reduce the constituent type set to only include types that aren't subtypes
    // of other types. Subtype reduction is expensive for large union types and is possible only when union
    // types are known not to circularly reference themselves (as is the case with union types created by
    // expression constructs such as array literals and the || and ?: operators). Named types can
    // circularly reference themselves and therefore cannot be subtype reduced during their declaration.
    // For example, "type Item = string | (() => Item" is a named type that circularly references itself.
    /// <summary>Go: <c>func (c *Checker) getUnionTypeEx(types []*Type, unionReduction UnionReduction, alias *TypeAlias, origin *Type) *Type</c>（checker.go:26032-26055）。</summary>
    internal Type GetUnionTypeExWorker(Type[] types, UnionReduction unionReduction, TypeAlias? alias, Type? origin)
    {
        if (types.Length == 0)
        {
            return _neverType;
        }

        if (types.Length == 1)
        {
            return types[0];
        }

        // We optimize for the common case of unioning a union type with some other type (such as `undefined`).
        if (types.Length == 2 && origin == null && ((types[0]._flags & TypeFlags.Union) != 0 || (types[1]._flags & TypeFlags.Union) != 0))
        {
            var id1 = types[0]._id;
            var id2 = types[1]._id;
            if (id1.CompareTo(id2) > 0)
            {
                (id1, id2) = (id2, id1);
            }

            var key = new UnionOfUnionKey(id1, id2, unionReduction, GetAliasKey(alias));
            _unionOfUnionTypes.TryGetValue(key, out var t);
            if (t == null)
            {
                t = GetUnionTypeWorkerImpl(types, unionReduction, alias, null /*origin*/);
                _unionOfUnionTypes[key] = t;
            }

            return t;
        }

        return GetUnionTypeWorkerImpl(types, unionReduction, alias, origin);
    }

    /// <summary>Go: <c>func (c *Checker) getUnionTypeWorker(types []*Type, unionReduction UnionReduction, alias *TypeAlias, origin *Type) *Type</c>（checker.go:26057-26138）。C# 名见文件头第 2 条。</summary>
    internal Type GetUnionTypeWorkerImpl(Type[] types, UnionReduction unionReduction, TypeAlias? alias, Type? origin)
    {
        var added = AddTypesToUnion(types);
        Type[]? typeSet = added.types;
        var includes = added.includes;
        if (unionReduction != UnionReduction.None)
        {
            if ((includes & TypeFlags.AnyOrUnknown) != 0)
            {
                if ((includes & TypeFlags.Any) != 0)
                {
                    if ((includes & TypeFlags.IncludesWildcard) != 0)
                    {
                        return _wildcardType;
                    }

                    if ((includes & TypeFlags.IncludesError) != 0)
                    {
                        return _errorType;
                    }

                    return _anyType;
                }

                return _unknownType;
            }

            if ((includes & TypeFlags.Undefined) != 0)
            {
                // If type set contains both undefinedType and missingType, remove missingType
                if (typeSet.Length >= 2 && typeSet[0] == _undefinedType && typeSet[1] == _missingType)
                {
                    typeSet = Splice(typeSet, 1, 1);
                }
            }

            if ((includes & (TypeFlags.Enum | TypeFlags.Literal | TypeFlags.UniqueESSymbol | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 ||
                (includes & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0)
            {
                typeSet = RemoveRedundantLiteralTypes(typeSet, includes, (unionReduction & UnionReduction.Subtype) != 0);
            }

            if ((includes & TypeFlags.StringLiteral) != 0 && (includes & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0)
            {
                typeSet = RemoveStringLiteralsMatchedByTemplateLiterals(typeSet);
            }

            if ((includes & TypeFlags.IncludesConstrainedTypeVariable) != 0)
            {
                typeSet = RemoveConstrainedTypeVariables(typeSet);
            }

            if (unionReduction == UnionReduction.Subtype)
            {
                typeSet = RemoveSubtypes(typeSet, (includes & TypeFlags.Object) != 0);
                if (typeSet == null)
                {
                    return _errorType;
                }
            }

            if (typeSet.Length == 0)
            {
                if ((includes & TypeFlags.Null) != 0)
                {
                    if ((includes & TypeFlags.IncludesNonWideningType) != 0)
                    {
                        return _nullType;
                    }

                    return _nullWideningType;
                }

                if ((includes & TypeFlags.Undefined) != 0)
                {
                    if ((includes & TypeFlags.IncludesNonWideningType) != 0)
                    {
                        return _undefinedType;
                    }

                    return _undefinedWideningType;
                }

                return _neverType;
            }
        }

        if (origin == null && (includes & TypeFlags.Union) != 0)
        {
            var namedUnions = AddNamedUnions(null, types);
            var reducedTypes = new List<Type>();
            foreach (var t in typeSet)
            {
                if (!Some(namedUnions, u => ContainsType(u.Types(), t)))
                {
                    reducedTypes.Add(t);
                }
            }

            if (alias == null && namedUnions.Length == 1 && reducedTypes.Count == 0)
            {
                return namedUnions[0];
            }

            // We create a denormalized origin type only when the union was created from one or more named unions
            // (unions with alias symbols or origins) and when there is no overlap between those named unions.
            var namedTypesCount = 0;
            foreach (var u in namedUnions)
            {
                namedTypesCount += u.Types().Length;
            }

            if (namedTypesCount + reducedTypes.Count == typeSet.Length)
            {
                var reduced = reducedTypes.ToArray();
                foreach (var t in namedUnions)
                {
                    TryInsertType(reduced, t, out var inserted);
                    reduced = inserted;
                }

                origin = NewUnionType(ObjectFlags.None, reduced);
            }
        }

        var objectFlags = IfElse((includes & TypeFlags.NotPrimitiveUnion) != 0, ObjectFlags.None, ObjectFlags.PrimitiveUnion) |
            IfElse((includes & TypeFlags.Intersection) != 0, ObjectFlags.ContainsIntersections, ObjectFlags.None);
        return GetUnionTypeFromSortedList(typeSet, objectFlags, alias, origin);
    }

    // This function assumes the constituent type list is sorted and deduplicated.
    /// <summary>Go: <c>func (c *Checker) getUnionTypeFromSortedList(types []*Type, precomputedObjectFlags ObjectFlags, alias *TypeAlias, origin *Type) *Type</c>（checker.go:26140-26159）。</summary>
    internal Type GetUnionTypeFromSortedList(Type[] types, ObjectFlags precomputedObjectFlags, TypeAlias? alias, Type? origin)
    {
        if (types.Length == 0)
        {
            return _neverType;
        }

        if (types.Length == 1)
        {
            return types[0];
        }

        var key = GetUnionKey(types, origin, alias);
        _unionTypes.TryGetValue(key, out var t);
        if (t == null)
        {
            t = NewUnionType(precomputedObjectFlags | GetPropagatingFlagsOfTypes(types, TypeFlags.Nullable), types);
            t.AsUnionType()!._origin = origin;
            t._alias = alias;
            if (types.Length == 2 && (types[0]._flags & TypeFlags.BooleanLiteral) != 0 && (types[1]._flags & TypeFlags.BooleanLiteral) != 0)
            {
                t._flags |= TypeFlags.Boolean;
            }

            _unionTypes[key] = t;
        }

        return t;
    }

    /// <summary>Go: <c>func (c *Checker) UnionTypes() iter.Seq[*Type]</c>（checker.go:26161-26163）。</summary>
    public IEnumerable<Type> UnionTypes()
    {
        return _unionTypes.Values;
    }

    /// <summary>Go: <c>func (c *Checker) addTypesToUnion(sourceTypes []*Type) ([]*Type, TypeFlags)</c>（checker.go:26165-26226）。</summary>
    internal (Type[] types, TypeFlags includes) AddTypesToUnion(Type[] sourceTypes)
    {
        var types = new List<Type>(sourceTypes.Length);
        var includes = TypeFlags.None;
        void AddType(Type t)
        {
            var flags = t._flags;
            // We ignore 'never' types in unions
            if ((flags & TypeFlags.Never) != 0)
            {
                return;
            }

            includes |= flags & TypeFlags.IncludesMask;
            if ((flags & TypeFlags.Instantiable) != 0)
            {
                includes |= TypeFlags.IncludesInstantiable;
            }

            if ((flags & TypeFlags.Intersection) != 0 && (t._objectFlags & ObjectFlags.IsConstrainedTypeVariable) != 0)
            {
                includes |= TypeFlags.IncludesConstrainedTypeVariable;
            }

            if (t == _wildcardType)
            {
                includes |= TypeFlags.IncludesWildcard;
            }

            if (IsErrorType(t))
            {
                includes |= TypeFlags.IncludesError;
            }

            if (!_strictNullChecks && (flags & TypeFlags.Nullable) != 0)
            {
                if ((t._objectFlags & ObjectFlags.ContainsWideningType) == 0)
                {
                    includes |= TypeFlags.IncludesNonWideningType;
                }

                return;
            }

            types.Add(t);
        }

        Type? lastType = null;
        foreach (var t in sourceTypes)
        {
            if (t != lastType)
            {
                if ((t._flags & TypeFlags.Union) != 0)
                {
                    var u = t.AsUnionType()!;
                    if (t._alias != null || u._origin != null)
                    {
                        includes |= TypeFlags.Union;
                    }

                    foreach (var s in u.Types())
                    {
                        AddType(s);
                    }
                }
                else
                {
                    AddType(t);
                }

                lastType = t;
            }
        }

        if (types.Count >= 2)
        {
            // Sort and deduplicate types
            var sorted = types.ToArray();
            Array.Sort(sorted, (a, b) => CompareTypes(a, b));
            var unique = 1;
            for (var i = 1; i < sorted.Length; i++)
            {
                var t = sorted[i];
                if (t != sorted[unique - 1])
                {
                    sorted[unique] = t;
                    unique++;
                }
            }

            Array.Resize(ref sorted, unique);
            return (sorted, includes);
        }

        return (types.ToArray(), includes);
    }

    /// <summary>Go: <c>func (c *Checker) addNamedUnions(namedUnions []*Type, types []*Type) []*Type</c>（checker.go:26228-26240）。</summary>
    internal Type[] AddNamedUnions(Type[]? namedUnions, Type[] types)
    {
        foreach (var t in types)
        {
            if ((t._flags & TypeFlags.Union) != 0)
            {
                var u = t.AsUnionType()!;
                if (t._alias != null || u._origin != null && (u._origin._flags & TypeFlags.Union) == 0)
                {
                    namedUnions = AppendIfUnique(namedUnions, t);
                }
                else if (u._origin != null && (u._origin._flags & TypeFlags.Union) != 0)
                {
                    namedUnions = AddNamedUnions(namedUnions, u._origin.Types());
                }
            }
        }

        return namedUnions ?? Array.Empty<Type>();
    }

    /// <summary>Go: <c>func (c *Checker) removeRedundantLiteralTypes(types []*Type, includes TypeFlags, reduceVoidUndefined bool) []*Type</c>（checker.go:26242-26259）。</summary>
    internal Type[] RemoveRedundantLiteralTypes(Type[] types, TypeFlags includes, bool reduceVoidUndefined)
    {
        var i = types.Length;
        while (i > 0)
        {
            i--;
            var t = types[i];
            var flags = t._flags;
            var remove = (flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && (includes & TypeFlags.String) != 0 ||
                (flags & TypeFlags.NumberLiteral) != 0 && (includes & TypeFlags.Number) != 0 ||
                (flags & TypeFlags.BigIntLiteral) != 0 && (includes & TypeFlags.BigInt) != 0 ||
                (flags & TypeFlags.UniqueESSymbol) != 0 && (includes & TypeFlags.ESSymbol) != 0 ||
                reduceVoidUndefined && (flags & TypeFlags.Undefined) != 0 && (includes & TypeFlags.Void) != 0 ||
                IsFreshLiteralType(t) && ContainsType(types, t.AsLiteralType()!._regularType!);
            if (remove)
            {
                types = Splice(types, i, 1);
            }
        }

        return types;
    }

    /// <summary>Go: <c>func (c *Checker) removeStringLiteralsMatchedByTemplateLiterals(types []*Type) []*Type</c>（checker.go:26261-26276）。</summary>
    internal Type[] RemoveStringLiteralsMatchedByTemplateLiterals(Type[] types)
    {
        var templates = Filter(types, IsPatternLiteralType);
        if (templates.Length != 0)
        {
            var i = types.Length;
            while (i > 0)
            {
                i--;
                var t = types[i];
                if ((t._flags & TypeFlags.StringLiteral) != 0 && Some(templates, template => IsTypeMatchedByTemplateLiteralOrStringMapping(t, template)))
                {
                    types = Splice(types, i, 1);
                }
            }
        }

        return types;
    }

    /// <summary>Go: <c>func (c *Checker) isTypeMatchedByTemplateLiteralOrStringMapping(t *Type, template *Type) bool</c>（checker.go:26278-26283）。</summary>
    internal bool IsTypeMatchedByTemplateLiteralOrStringMapping(Type t, Type template)
    {
        if ((template._flags & TypeFlags.TemplateLiteral) != 0)
        {
            return IsTypeMatchedByTemplateLiteralType(t, template.AsTemplateLiteralType()!, _compareTypesAssignable);
        }

        return IsMemberOfStringMapping(t, template);
    }
}
