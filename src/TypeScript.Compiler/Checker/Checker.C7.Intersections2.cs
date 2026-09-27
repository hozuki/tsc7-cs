// ported from: internal/checker/checker.go:26662-27083（交集成员化简 + 类型集合谓词 + 叉积）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `(切片, bool)` 双返回值 → `bool TryXxx(..., out T[] result)`（brief §5.3）：
//    `extractRedundantTemplateLiterals` → `TryExtractRedundantTemplateLiterals`、
//    `intersectUnionsOfPrimitiveTypes` → `TryIntersectUnionsOfPrimitiveTypes`。
// 2) `slices.Delete(types, i, i+1)` → `Core.Splice(types, i, 1)`（都是"去掉 i 处元素"的新切片）。
// 3) `slices.Clone(types)` → `(Type[])types.Clone()`；`slices.Insert(types, i, t)` 就地手写拷贝。
// 4) `slices.BinarySearchFunc(x, t, CompareTypes)` 在 Go 里返回**最左**命中下标（未命中返回插入点），
//    .NET 的 Array.BinarySearch 不保证最左，故本文件私有 `TryBinarySearchTypes` 逐行复刻 Go 的语义。
// 5) Go 的 `t.AsType()`（在 `*StructuredType` 上取内嵌的 `*Type`）→ 直接用 `t`
//    （C# 里 StructuredType 本身就继承自 Type）。
// 6) `getConstituentCount` 的 Go 无条件 `switch { case A: ...; case B: ... }` → `if/if`（语义相同）。
// 7) `math.MaxInt` → `int.MaxValue`；`for i := range count` → `for (var i = 0; i < count; i++)`。
// 8) 局部名 `checked`（C# 关键字）→ `checkedTypes`；Go 的局部 `result` → `intersections`
//    （`result` 被 out 形参占用，Go 的局部 `result` 是"各 union 的交集"结果集）。
// 9) Go 的实例方法值（`c.isEmptyObjectType` 等）→ lambda（与 C6/C4 已落地文件的写法一致）。
// 10) **usings 必须写在 file-scoped `namespace TypeScript.Checker;` 之后**：写在之前时裸名 `Diagnostics`
//     会先命中外层命名空间 `TypeScript.Diagnostics`（编译单元级 using-alias 也无法覆盖）→ CS0234；
//     usings 在 namespace 声明体内时，导入的类型优先（最小探针实测，与 Checker 包其它文件一致）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) addTypeToIntersection(typeSet *orderedSet[*Type], includes TypeFlags, t *Type) TypeFlags</c>（checker.go:26662-26689）。</summary>
    internal TypeFlags AddTypeToIntersection(SmallOrderedSet<Type> typeSet, TypeFlags includes, Type t)
    {
        var flags = t.Flags();
        if ((flags & TypeFlags.Intersection) != 0)
        {
            return AddTypesToIntersection(typeSet, includes, t.Types());
        }

        if (IsEmptyAnonymousObjectType(t))
        {
            if ((includes & TypeFlags.IncludesEmptyObject) == 0)
            {
                includes |= TypeFlags.IncludesEmptyObject;
                typeSet.Add(t);
            }
        }
        else
        {
            if ((flags & TypeFlags.AnyOrUnknown) != 0)
            {
                if (t == _wildcardType)
                {
                    includes |= TypeFlags.IncludesWildcard;
                }

                if (IsErrorType(t))
                {
                    includes |= TypeFlags.IncludesError;
                }
            }
            else if (_strictNullChecks || (flags & TypeFlags.Nullable) == 0)
            {
                if (t == _missingType)
                {
                    includes |= TypeFlags.IncludesMissingType;
                    t = _undefinedType;
                }

                if (!typeSet.Contains(t))
                {
                    if ((t.Flags() & TypeFlags.Unit) != 0 && (includes & TypeFlags.Unit) != 0)
                    {
                        // We have seen two distinct unit types which means we should reduce to an
                        // empty intersection. Adding TypeFlags.NonPrimitive causes that to happen.
                        includes |= TypeFlags.NonPrimitive;
                    }

                    typeSet.Add(t);
                }
            }

            includes |= flags & TypeFlags.IncludesMask;
        }

        return includes;
    }

    /// <summary>Go: <c>func (c *Checker) removeRedundantSupertypes(types []*Type, includes TypeFlags) []*Type</c>（checker.go:26699-26719）。</summary>
    internal Type[] RemoveRedundantSupertypes(Type[] types, TypeFlags includes)
    {
        var i = types.Length;
        while (i > 0)
        {
            i--;
            var t = types[i];
            var remove = (t.Flags() & TypeFlags.String) != 0 && (includes & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 ||
                (t.Flags() & TypeFlags.Number) != 0 && (includes & TypeFlags.NumberLiteral) != 0 ||
                (t.Flags() & TypeFlags.BigInt) != 0 && (includes & TypeFlags.BigIntLiteral) != 0 ||
                (t.Flags() & TypeFlags.ESSymbol) != 0 && (includes & TypeFlags.UniqueESSymbol) != 0 ||
                (t.Flags() & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0 ||
                IsEmptyAnonymousObjectType(t) && (includes & TypeFlags.DefinitelyNonNullable) != 0;
            if (remove)
            {
                // Go: types = slices.Delete(types, i, i+1)
                types = Splice(types, i, 1);
            }
        }

        return types;
    }

    /// <summary>
    /// Returns true if the intersection of the template literals and string literals is the empty set,
    /// for example `get${string}` &amp; "setX", and should reduce to never.
    /// Go: <c>func (c *Checker) extractRedundantTemplateLiterals(types []*Type) ([]*Type, bool)</c>（checker.go:26721-26745）。
    /// </summary>
    internal bool TryExtractRedundantTemplateLiterals(Type[] types, out Type[] result)
    {
        var literals = Filter(types, t => (t.Flags() & TypeFlags.StringLiteral) != 0);
        var i = types.Length;
        while (i > 0)
        {
            i--;
            var t = types[i];
            if ((t.Flags() & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) == 0)
            {
                continue;
            }

            foreach (var t2 in literals)
            {
                if (IsTypeSubtypeOf(t2, t))
                {
                    // For example, `get${T}` & "getX" is just "getX", and Lowercase<string> & "foo" is just "foo"
                    // Go: types = slices.Delete(types, i, i+1)
                    types = Splice(types, i, 1);
                    break;
                }

                if (IsPatternLiteralType(t))
                {
                    result = types;
                    return true;
                }
            }
        }

        result = types;
        return false;
    }

    /// <summary>
    /// If the given list of types contains more than one union of primitive types, replace the
    /// first with a union containing an intersection of those primitive types, then remove the
    /// other unions and return true. Otherwise, do nothing and return false.
    /// Go: <c>func (c *Checker) intersectUnionsOfPrimitiveTypes(types []*Type) ([]*Type, bool)</c>（checker.go:26747-26774）。
    /// </summary>
    internal bool TryIntersectUnionsOfPrimitiveTypes(Type[] types, out Type[] result)
    {
        var index = Array.FindIndex(types, t => IsPrimitiveUnion(t));
        if (index < 0)
        {
            result = types;
            return false;
        }

        // Remove all but the first union of primitive types and collect them in
        // the unionTypes array.
        var i = index + 1;
        // Go: types[index:i:i]（三索引切片：len == cap，后续 append 必然重新分配）
        var unionTypeList = new List<Type>();
        for (var k = index; k < i; k++)
        {
            unionTypeList.Add(types[k]);
        }

        while (i < types.Length)
        {
            var t = types[i];
            if ((t.ObjectFlags() & ObjectFlags.PrimitiveUnion) != 0)
            {
                unionTypeList.Add(t);
                // Go: types = slices.Delete(types, i, i+1)
                types = Splice(types, i, 1);
            }
            else
            {
                i++;
            }
        }

        var unionTypes = unionTypeList.ToArray();

        // Return false if there was only one union of primitive types
        if (unionTypes.Length == 1)
        {
            result = types;
            return false;
        }

        // We have more than one union of primitive types, now intersect them. For each
        // type in each union we check if the type is matched in every union and if so
        // we include it in the result.
        var checkedTypes = Array.Empty<Type>();
        var intersections = Array.Empty<Type>();
        foreach (var u in unionTypes)
        {
            foreach (var t in u.Types())
            {
                if (TryInsertType(checkedTypes, t, out checkedTypes))
                {
                    if (EachUnionContains(unionTypes, t))
                    {
                        // undefinedType/missingType are always sorted first so we leverage that here
                        if (t == _undefinedType && intersections.Length != 0 && intersections[0] == _missingType)
                        {
                            continue;
                        }

                        if (t == _missingType && intersections.Length != 0 && intersections[0] == _undefinedType)
                        {
                            intersections[0] = _missingType;
                            continue;
                        }

                        TryInsertType(intersections, t, out intersections);
                    }
                }
            }
        }

        // Finally replace the first union with the result
        types[index] = GetUnionTypeFromSortedList(intersections, ObjectFlags.PrimitiveUnion, null /*alias*/, null /*origin*/);
        result = types;
        return true;
    }

    /// <summary>
    /// Check that the given type has a match in every union. A given type is matched by
    /// an identical type, and a literal type is additionally matched by its corresponding
    /// primitive type, and missingType is matched by undefinedType (and vice versa).
    /// Go: <c>func (c *Checker) eachUnionContains(unionTypes []*Type, t *Type) bool</c>（checker.go:26776-26784）。
    /// </summary>
    internal bool EachUnionContains(Type[] unionTypes, Type t)
    {
        foreach (var u in unionTypes)
        {
            if (!UnionContainsType(u, t, true /*matchSymbol*/))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>func (c *Checker) unionContainsType(union *Type, t *Type, matchSymbol bool) bool</c>（checker.go:26786-26805）。</summary>
    internal bool UnionContainsType(Type union, Type t, bool matchSymbol)
    {
        var types = union.Types();
        if (ContainsType(types, t))
        {
            return true;
        }

        if (t == _missingType)
        {
            return ContainsType(types, _undefinedType);
        }

        if (t == _undefinedType)
        {
            return ContainsType(types, _missingType);
        }

        Type? primitive;
        if ((t.Flags() & TypeFlags.StringLiteral) != 0)
        {
            primitive = _stringType;
        }
        else if ((t.Flags() & (TypeFlags.Enum | TypeFlags.NumberLiteral)) != 0)
        {
            primitive = _numberType;
        }
        else if ((t.Flags() & TypeFlags.BigIntLiteral) != 0)
        {
            primitive = _bigintType;
        }
        else if ((t.Flags() & TypeFlags.UniqueESSymbol) != 0 && matchSymbol)
        {
            primitive = _esSymbolType;
        }
        else
        {
            primitive = null;
        }

        return primitive != null && ContainsType(types, primitive);
    }

    /// <summary>Go: <c>func (c *Checker) getCrossProductIntersections(types []*Type, flags IntersectionFlags) []*Type</c>（checker.go:26807-26824）。</summary>
    internal Type[] GetCrossProductIntersections(Type[] types, IntersectionFlags flags)
    {
        var count = GetCrossProductUnionSize(types);
        var intersections = new List<Type>();
        for (var i = 0; i < count; i++)
        {
            // Go: constituents := slices.Clone(types)
            var constituents = (Type[])types.Clone();
            var n = i;
            for (var j = types.Length - 1; j >= 0; j--)
            {
                if ((types[j].Flags() & TypeFlags.Union) != 0)
                {
                    var sourceTypes = types[j].Types();
                    var length = sourceTypes.Length;
                    constituents[j] = sourceTypes[n % length];
                    n = n / length;
                }
            }

            var t = GetIntersectionTypeEx(constituents, flags, null /*alias*/);
            if ((t.Flags() & TypeFlags.Never) == 0)
            {
                intersections.Add(t);
            }
        }

        return intersections.ToArray();
    }

    /// <summary>Go: <c>func getConstituentCount(t *Type) int</c>（checker.go:26826-26835）。</summary>
    internal static int GetConstituentCount(Type t)
    {
        if ((t.Flags() & TypeFlags.UnionOrIntersection) == 0 || t.Alias() != null)
        {
            return 1;
        }

        if ((t.Flags() & TypeFlags.Union) != 0 && t.AsUnionType()!._origin != null)
        {
            return GetConstituentCount(t.AsUnionType()!._origin!);
        }

        return GetConstituentCountOfTypes(t.Types());
    }

    /// <summary>Go: <c>func getConstituentCountOfTypes(types []*Type) int</c>（checker.go:26837-26843）。</summary>
    internal static int GetConstituentCountOfTypes(Type[] types)
    {
        var n = 0;
        foreach (var t in types)
        {
            n += GetConstituentCount(t);
        }

        return n;
    }

    /// <summary>Go: <c>func (c *Checker) filterTypes(types []*Type, predicate func(*Type) bool)</c>（checker.go:26845-26849）。</summary>
    internal void FilterTypes(Type[] types, Func<Type, bool> predicate)
    {
        for (var i = 0; i < types.Length; i++)
        {
            types[i] = FilterType(types[i], predicate);
        }
    }

    /// <summary>Go: <c>func (c *Checker) IsEmptyAnonymousObjectType(t *Type) bool</c>（checker.go:26851-26853）。</summary>
    public bool IsEmptyAnonymousObjectType(Type t)
    {
        return (t.ObjectFlags() & ObjectFlags.Anonymous) != 0 && ((t.ObjectFlags() & ObjectFlags.MembersResolved) != 0 && IsEmptyResolvedType(t.AsStructuredType()!) ||
            t.Symbol() != null && (t.Symbol()!.Flags & SymbolFlags.TypeLiteral) != 0 && (GetMembersOfSymbol(t.Symbol()!)?.Count ?? 0) == 0);
    }

    /// <summary>Go: <c>func (c *Checker) isEmptyResolvedType(t *StructuredType) bool</c>（checker.go:26855-26857）。</summary>
    internal bool IsEmptyResolvedType(StructuredType t)
    {
        // Go: t.AsType() != c.anyFunctionType（AsType 取的就是内嵌的那个 *Type）
        return t != _anyFunctionType && (t._properties?.Length ?? 0) == 0 && (t._signatures?.Length ?? 0) == 0 && (t._indexInfos?.Length ?? 0) == 0;
    }

    /// <summary>Go: <c>func (c *Checker) isEmptyObjectType(t *Type) bool</c>（checker.go:26860-26871）。</summary>
    internal bool IsEmptyObjectType(Type t)
    {
        if ((t.Flags() & TypeFlags.Object) != 0)
        {
            return !IsGenericMappedType(t) && IsEmptyResolvedType(ResolveStructuredTypeMembers(t));
        }

        if ((t.Flags() & TypeFlags.NonPrimitive) != 0)
        {
            return true;
        }

        if ((t.Flags() & TypeFlags.Union) != 0)
        {
            return Some(t.Types(), s => IsEmptyObjectType(s));
        }

        if ((t.Flags() & TypeFlags.Intersection) != 0)
        {
            return Every(t.Types(), s => IsEmptyObjectType(s));
        }

        return false;
    }

    /// <summary>Go: <c>func (c *Checker) isPatternLiteralPlaceholderType(t *Type) bool</c>（checker.go:26873-26888）。</summary>
    internal bool IsPatternLiteralPlaceholderType(Type t)
    {
        if ((t.Flags() & TypeFlags.Intersection) != 0)
        {
            // Return true if the intersection consists of one or more placeholders and zero or
            // more object type tags.
            var seenPlaceholder = false;
            foreach (var s in t.Types())
            {
                if ((s.Flags() & (TypeFlags.Literal | TypeFlags.Nullable)) != 0 || IsPatternLiteralPlaceholderType(s))
                {
                    seenPlaceholder = true;
                }
                else if ((s.Flags() & TypeFlags.Object) == 0)
                {
                    return false;
                }
            }

            return seenPlaceholder;
        }

        return (t.Flags() & (TypeFlags.Any | TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0 || IsPatternLiteralType(t);
    }

    /// <summary>Go: <c>func (c *Checker) isPatternLiteralType(t *Type) bool</c>（checker.go:26890-26894）。</summary>
    internal bool IsPatternLiteralType(Type t)
    {
        // A pattern literal type is a template literal or a string mapping type that contains only
        // non-generic pattern literal placeholders.
        return (t.Flags() & TypeFlags.TemplateLiteral) != 0 && Every(t.AsTemplateLiteralType()!._types, s => IsPatternLiteralPlaceholderType(s)) ||
            (t.Flags() & TypeFlags.StringMapping) != 0 && IsPatternLiteralPlaceholderType(t.Target()!);
    }

    /// <summary>Go: <c>func (c *Checker) isGenericStringLikeType(t *Type) bool</c>（checker.go:26896-26898）。</summary>
    internal bool IsGenericStringLikeType(Type t)
    {
        return (t.Flags() & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && !IsPatternLiteralType(t);
    }

    /// <summary>Go: <c>func forEachType(t *Type, f func(t *Type))</c>（checker.go:26900-26906）。</summary>
    internal static void ForEachType(Type t, Action<Type> f)
    {
        if ((t.Flags() & TypeFlags.Union) != 0)
        {
            foreach (var u in t.Types())
            {
                f(u);
            }
        }
        else
        {
            f(t);
        }
    }

    /// <summary>Go: <c>func someType(t *Type, f func(*Type) bool) bool</c>（checker.go:26908-26914）。</summary>
    internal static bool SomeType(Type t, Func<Type, bool> f)
    {
        if ((t.Flags() & TypeFlags.Union) != 0)
        {
            return Some(t.Types(), f);
        }

        return f(t);
    }

    /// <summary>Go: <c>func everyType(t *Type, f func(*Type) bool) bool</c>（checker.go:26916-26922）。</summary>
    internal static bool EveryType(Type t, Func<Type, bool> f)
    {
        if ((t.Flags() & TypeFlags.Union) != 0)
        {
            return Every(t.Types(), f);
        }

        return f(t);
    }

    /// <summary>Go: <c>func everyContainedType(t *Type, f func(*Type) bool) bool</c>（checker.go:26924-26930）。</summary>
    internal static bool EveryContainedType(Type t, Func<Type, bool> f)
    {
        if ((t.Flags() & TypeFlags.UnionOrIntersection) != 0)
        {
            return Every(t.Types(), f);
        }

        return f(t);
    }

    /// <summary>Go: <c>func (c *Checker) filterType(t *Type, f func(*Type) bool) *Type</c>（checker.go:26932-26973）。</summary>
    internal Type FilterType(Type t, Func<Type, bool> f)
    {
        if ((t.Flags() & TypeFlags.Union) != 0)
        {
            var types = t.Types();
            var filtered = Filter(types, f);
            if (Same(types, filtered))
            {
                return t;
            }

            var origin = t.AsUnionType()!._origin;
            Type? newOrigin = null;
            if (origin != null && (origin.Flags() & TypeFlags.Union) != 0)
            {
                // If the origin type is a (denormalized) union type, filter its non-union constituents. If that ends
                // up removing a smaller number of types than in the normalized constituent set (meaning some of the
                // filtered types are within nested unions in the origin), then we can't construct a new origin type.
                // Otherwise, if we have exactly one type left in the origin set, return that as the filtered type.
                // Otherwise, construct a new filtered origin type.
                var originTypes = origin.Types();
                var originFiltered = Filter(originTypes, u => (u.Flags() & TypeFlags.Union) != 0 || f(u));
                if (originTypes.Length - originFiltered.Length == types.Length - filtered.Length)
                {
                    if (originFiltered.Length == 1)
                    {
                        return originFiltered[0];
                    }

                    newOrigin = NewUnionType(ObjectFlags.None, originFiltered);
                }
            }

            // filtering could remove intersections so `ContainsIntersections` might be forwarded "incorrectly"
            // it is purely an optimization hint so there is no harm in accidentally forwarding it
            return GetUnionTypeFromSortedList(filtered, t.AsUnionType()!.ObjectFlags() & (ObjectFlags.PrimitiveUnion | ObjectFlags.ContainsIntersections), null /*alias*/, newOrigin);
        }

        if ((t.Flags() & TypeFlags.Never) != 0 || f(t))
        {
            return t;
        }

        return _neverType;
    }

    /// <summary>Go: <c>func (c *Checker) removeType(t *Type, targetType *Type) *Type</c>（checker.go:26975-26996）。</summary>
    internal Type RemoveType(Type t, Type targetType)
    {
        if ((t.Flags() & TypeFlags.Union) == 0)
        {
            if (t == targetType)
            {
                return _neverType;
            }

            return t;
        }

        var origin = t.AsUnionType()!._origin;
        if (origin != null && (origin.Flags() & TypeFlags.Union) != 0 && ContainsType(origin.Types(), targetType))
        {
            return FilterType(t, x => x != targetType);
        }

        var types = t.Types();
        if (TryBinarySearchTypes(types, targetType, out var i))
        {
            if (types.Length == 2)
            {
                return types[1 - i];
            }

            // Remove the target type from the slice.
            // Go: append(types[:i:i], types[i+1:]...)
            var filtered = Splice(types, i, 1);
            return GetUnionTypeFromSortedList(filtered, t.AsUnionType()!.ObjectFlags() & (ObjectFlags.PrimitiveUnion | ObjectFlags.ContainsIntersections), null /*alias*/, null /*origin*/);
        }

        return t;
    }

    /// <summary>Go: <c>func containsType(types []*Type, t *Type) bool</c>（checker.go:26998-27001）。</summary>
    internal static bool ContainsType(Type[] types, Type t)
    {
        return TryBinarySearchTypes(types, t, out _);
    }

    /// <summary>Go: <c>func insertType(types []*Type, t *Type) ([]*Type, bool)</c>（checker.go:27003-27008）。</summary>
    internal static bool TryInsertType(Type[] types, Type t, out Type[] result)
    {
        if (!TryBinarySearchTypes(types, t, out var i))
        {
            // Go: slices.Insert(types, i, t)
            var inserted = new Type[types.Length + 1];
            Array.Copy(types, inserted, i);
            inserted[i] = t;
            Array.Copy(types, i, inserted, i + 1, types.Length - i);
            result = inserted;
            return true;
        }

        result = types;
        return false;
    }

    /// <summary>Go: <c>func countTypes(t *Type) int</c>（checker.go:27010-27017）。</summary>
    internal static int CountTypes(Type t)
    {
        if ((t.Flags() & TypeFlags.Union) != 0)
        {
            return t.Types().Length;
        }

        if ((t.Flags() & TypeFlags.Never) != 0)
        {
            return 0;
        }

        return 1;
    }

    /// <summary>Go: <c>func (c *Checker) isErrorType(t *Type) bool</c>（checker.go:27019-27023）。</summary>
    internal bool IsErrorType(Type t)
    {
        // The only 'any' types that have alias symbols are those manufactured by getTypeFromTypeAliasReference for
        // a reference to an unresolved symbol. We want those to behave like the errorType.
        return t == _errorType || (t.Flags() & TypeFlags.Any) != 0 && t.Alias() != null;
    }

    /// <summary>Go: <c>func compareTypeIds(t1, t2 *Type) int</c>（checker.go:27025-27027）。</summary>
    internal static int CompareTypeIds(Type t1, Type t2)
    {
        return (int)t1._id.Value - (int)t2._id.Value;
    }

    /// <summary>Go: <c>func (c *Checker) checkCrossProductUnion(types []*Type) bool</c>（checker.go:27029-27039）。</summary>
    internal bool CheckCrossProductUnion(Type[] types)
    {
        var size = GetCrossProductUnionSize(types);
        if (size >= 100_000)
        {
            _tracer?.Instant(Phase.CheckTypes, "checkCrossProductUnion_DepthLimit", new Dictionary<string, object?> { ["size"] = size });
            Error(_currentNode, Diagnostics.Expression_produces_a_union_type_that_is_too_complex_to_represent);
            return false;
        }

        return true;
    }

    /// <summary>Go: <c>func (c *Checker) getCrossProductUnionSize(types []*Type) int</c>（checker.go:27047-27062）。</summary>
    internal int GetCrossProductUnionSize(Type[] types)
    {
        var size = 1;
        foreach (var t in types)
        {
            if ((t.Flags() & TypeFlags.Union) != 0)
            {
                var n = t.Types().Length;
                // Cap the result to avoid integer overflow when computing the cross product of many large unions.
                // In TypeScript, number overflow produces Infinity which naturally exceeds the limit check;
                // in Go, we must guard against int wrapping to zero or negative.
                if (n > 0 && size > int.MaxValue / n)
                {
                    return int.MaxValue;
                }

                size *= n;
            }
            else if ((t.Flags() & TypeFlags.Never) != 0)
            {
                return 0;
            }
        }

        return size;
    }

    /// <summary>Go: <c>slices.BinarySearchFunc(types, target, CompareTypes)</c>：返回最左命中下标与是否命中（未命中时 index 为插入点）。</summary>
    private static bool TryBinarySearchTypes(Type[] types, Type target, out int index)
    {
        var i = 0;
        var j = types.Length;
        while (i < j)
        {
            var h = i + ((j - i) >> 1);
            if (CompareTypes(types[h], target) < 0)
            {
                i = h + 1;
            }
            else
            {
                j = h;
            }
        }

        index = i;
        return i < types.Length && CompareTypes(types[i], target) == 0;
    }
}
