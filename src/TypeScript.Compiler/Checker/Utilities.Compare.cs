// ported from: internal/checker/utilities.go:350-756（符号/类型比较与排序：createSymbolTable … compareTypeMappers）
//
// 与 Go 的机械改写（本片特有的，通用规则见 Utilities.cs 文件头）：
// 1) `slices.SortFunc(symbols, c.compareSymbols)` → `Array.Sort(symbols, ...)`（同为核心库的就地不稳定排序）。
// 2) `strings.Compare(a, b)` → `string.CompareOrdinal(a, b)`。Go 的 strings.Compare 是**字节序**比较；UTF-8 字节序与
//    UTF-16 代码单元序都随码点单调，二者对任意合法字符串给出同一排序（含孤立代理项与 astral 码点）。
// 3) `int(ast.GetSymbolId(s1)) - int(ast.GetSymbolId(s2))` → `GetSymbolId(s1).CompareTo(GetSymbolId(s2))`
//    （SymbolId 实现 IComparable<SymbolId>；Go 的写法在大 ID 上会溢出 int，这里不会，排序结果一致）。
// 4) `slices.Compare(t1.texts, t2.texts)` → `SequenceCompareTo`（MemoryExtensions 对 IComparable<T> 元素的
//    字典序 + 长度 tiebreak，与 slices.Compare 语义一致）。
// 5) 类型断言 `x.data.(*X)` → `(X)x`（docs/checker-plan.md §5.1）；`AsX()` 返回可空 → 断言处补 `!`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    /// <summary>Go: <c>func createSymbolTable(symbols []*ast.Symbol) ast.SymbolTable</c>（utilities.go:350-359）。</summary>
    internal static SymbolTable? CreateSymbolTable(Symbol[]? symbols)
    {
        if (symbols is null || symbols.Length == 0)
        {
            return null;
        }
        var result = new SymbolTable();
        foreach (var symbol in symbols)
        {
            result[symbol.Name] = symbol;
        }
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) sortSymbols(symbols []*ast.Symbol)</c>（utilities.go:361-363）。</summary>
    internal void SortSymbols(Symbol[] symbols)
    {
        Array.Sort(symbols, (s1, s2) => _compareSymbols(s1, s2));
    }

    /// <summary>Go: <c>func (c *Checker) compareSymbolsWorker(s1, s2 *ast.Symbol) int</c>（utilities.go:365-390）。</summary>
    internal int CompareSymbolsWorker(Symbol? s1, Symbol? s2)
    {
        if (s1 == s2)
        {
            return 0;
        }
        if (s1 is null)
        {
            return 1;
        }
        if (s2 is null)
        {
            return -1;
        }
        var d1 = s1.Declarations?.Length ?? 0;
        var d2 = s2.Declarations?.Length ?? 0;
        if (d1 != 0 && d2 != 0)
        {
            var r = CompareNodes(s1.Declarations![0], s2.Declarations![0]);
            if (r != 0)
            {
                return r;
            }
        }
        else if (d1 != 0)
        {
            return -1;
        }
        else if (d2 != 0)
        {
            return 1;
        }
        var c = string.CompareOrdinal(s1.Name, s2.Name);
        if (c != 0)
        {
            return c;
        }
        // Fall back to symbol IDs. This is a last resort that should happen only when symbols have
        // no declaration and duplicate names.
        return GetSymbolId(s1).CompareTo(GetSymbolId(s2));
    }

    /// <summary>Go: <c>func (c *Checker) compareNodes(n1, n2 *ast.Node) int</c>（utilities.go:392-412）。</summary>
    internal int CompareNodes(Node? n1, Node? n2)
    {
        if (n1 == n2)
        {
            return 0;
        }
        if (n1 is null)
        {
            return 1;
        }
        if (n2 is null)
        {
            return -1;
        }
        var s1 = GetSourceFileOfNode(n1);
        var s2 = GetSourceFileOfNode(n2);
        if (s1 != s2)
        {
            // Go 的 `c.fileIndexMap[s1]`：map 读缺失键（含 nil 键）返回 0，C# 必须显式判 null。
            var f1 = s1 is not null ? _fileIndexMap.GetValueOrDefault(s1) : 0;
            var f2 = s2 is not null ? _fileIndexMap.GetValueOrDefault(s2) : 0;
            // Order by index of file in the containing program
            return f1 - f2;
        }
        // In the same file, order by source position
        return n1.Pos() - n2.Pos();
    }

    /// <summary>Go: <c>func CompareTypes(t1, t2 *Type) int</c>（utilities.go:414-622）。</summary>
    public static int CompareTypes(Type? t1, Type? t2)
    {
        if (t1 == t2)
        {
            return 0;
        }
        if (t1 is null)
        {
            return -1;
        }
        if (t2 is null)
        {
            return 1;
        }
        if (t1._checker != t2._checker)
        {
            Debug.Fail("Cannot compare types from different checkers");
        }
        // First sort in order of increasing type flags values.
        var c = GetSortOrderFlags(t1) - GetSortOrderFlags(t2);
        if (c != 0)
        {
            return c;
        }
        // Order named types by name and, in the case of aliased types, by alias type arguments.
        c = CompareTypeNames(t1, t2);
        if (c != 0)
        {
            return c;
        }
        // We have unnamed types or types with identical names. Now sort by data specific to the type.
        if ((t1._flags & (TypeFlags.Any | TypeFlags.Unknown | TypeFlags.String | TypeFlags.Number | TypeFlags.Boolean | TypeFlags.BigInt | TypeFlags.ESSymbol | TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Null | TypeFlags.Never | TypeFlags.NonPrimitive)) != 0)
        {
            // Only distinguished by type IDs, handled below.
        }
        else if ((t1._flags & TypeFlags.Object) != 0)
        {
            // Order instantiation expression types without relying on lazy symbol IDs.
            // Order other unnamed or identically named object types by symbol.
            if ((t1._objectFlags & ObjectFlags.InstantiationExpressionType) != 0 && (t2._objectFlags & ObjectFlags.InstantiationExpressionType) != 0)
            {
                Node? declaration1 = null;
                Node? declaration2 = null;
                if (t1._symbol is not null && (t1._symbol.Declarations?.Length ?? 0) != 0)
                {
                    declaration1 = t1._symbol.Declarations![0];
                }
                if (t2._symbol is not null && (t2._symbol.Declarations?.Length ?? 0) != 0)
                {
                    declaration2 = t2._symbol.Declarations![0];
                }
                // A single instantiation expression can produce multiple types for union constituents,
                // so compare their source declarations before comparing the shared expression node.
                c = t1._checker.CompareNodes(declaration1, declaration2);
                if (c != 0)
                {
                    return c;
                }
                c = t1._checker.CompareNodes(t1.AsInstantiationExpressionType()!._node, t2.AsInstantiationExpressionType()!._node);
                if (c != 0)
                {
                    return c;
                }
            }
            else
            {
                c = t1._checker.CompareSymbols(t1._symbol!, t2._symbol!);
                if (c != 0)
                {
                    return c;
                }
            }
            // When object types have the same or no symbol, order by kind. We order type references before other kinds.
            if ((t1._objectFlags & ObjectFlags.Reference) != 0 && (t2._objectFlags & ObjectFlags.Reference) != 0)
            {
                var r1 = t1.AsTypeReference()!;
                var r2 = t2.AsTypeReference()!;
                if ((r1._target!._objectFlags & ObjectFlags.Tuple) != 0 && (r2._target!._objectFlags & ObjectFlags.Tuple) != 0)
                {
                    // Tuple types have no associated symbol, instead we order by tuple element information.
                    c = CompareTupleTypes(r1._target!.AsTupleType()!, r2._target!.AsTupleType()!);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                // Here we know we have references to instantiations of the same type because we have matching targets.
                if (r1._node is null && r2._node is null)
                {
                    // Non-deferred type references with the same target are sorted by their type argument lists.
                    c = CompareTypeLists(t1.AsTypeReference()!._resolvedTypeArguments, t2.AsTypeReference()!._resolvedTypeArguments);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                else
                {
                    // Deferred type references with the same target are ordered by the source location of the reference.
                    c = t1._checker.CompareNodes(r1._node, r2._node);
                    if (c != 0)
                    {
                        return c;
                    }
                    // Instantiations of the same deferred type reference are ordered by their associated type mappers
                    // (which reflect the mapping of in-scope type parameters to type arguments).
                    c = CompareTypeMappers(t1.AsObjectType()!._mapper, t2.AsObjectType()!._mapper);
                    if (c != 0)
                    {
                        return c;
                    }
                }
            }
            else if ((t1._objectFlags & ObjectFlags.Reference) != 0)
            {
                return -1;
            }
            else if ((t2._objectFlags & ObjectFlags.Reference) != 0)
            {
                return 1;
            }
            else
            {
                // Order unnamed non-reference object types by kind and instantiation data.
                c = (int)(t1._objectFlags & ObjectFlags.ObjectTypeKindMask) - (int)(t2._objectFlags & ObjectFlags.ObjectTypeKindMask);
                if (c != 0)
                {
                    return c;
                }
                if ((t1._objectFlags & ObjectFlags.ReverseMapped) != 0)
                {
                    var rm1 = t1.AsReverseMappedType()!;
                    var rm2 = t2.AsReverseMappedType()!;
                    c = CompareTypes(rm1._source, rm2._source);
                    if (c != 0)
                    {
                        return c;
                    }
                    c = CompareTypes(rm1._mappedType, rm2._mappedType);
                    if (c != 0)
                    {
                        return c;
                    }
                    c = CompareTypes(rm1._constraintType, rm2._constraintType);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                var m1 = t1.AsObjectType()!._mapper;
                var m2 = t2.AsObjectType()!._mapper;
                if ((t1._objectFlags & ObjectFlags.Mapped) != 0)
                {
                    // instantiateAnonymousType prepends a fresh type parameter mapping.
                    // Compare the effective instantiation, not the identity of that fresh parameter.
                    if (m1 is not null)
                    {
                        m1 = ((CompositeTypeMapper)m1)._m2;
                    }
                    if (m2 is not null)
                    {
                        m2 = ((CompositeTypeMapper)m2)._m2;
                    }
                }
                c = CompareTypeMappers(m1, m2);
                if (c != 0)
                {
                    return c;
                }
            }
        }
        else if ((t1._flags & TypeFlags.Union) != 0)
        {
            // Unions are ordered by origin and then constituent type lists.
            var o1 = t1.AsUnionType()!._origin;
            var o2 = t2.AsUnionType()!._origin;
            if (o1 is null && o2 is null)
            {
                c = CompareTypeLists(t1.Types(), t2.Types());
                if (c != 0)
                {
                    return c;
                }
            }
            else if (o1 is null)
            {
                return 1;
            }
            else if (o2 is null)
            {
                return -1;
            }
            else
            {
                c = CompareTypes(o1, o2);
                if (c != 0)
                {
                    return c;
                }
            }
        }
        else if ((t1._flags & TypeFlags.Intersection) != 0)
        {
            // Intersections are ordered by their constituent type lists.
            c = CompareTypeLists(t1.Types(), t2.Types());
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & (TypeFlags.Enum | TypeFlags.EnumLiteral | TypeFlags.UniqueESSymbol)) != 0)
        {
            // Enum members are ordered by their symbol (and thus their declaration order).
            c = t1._checker.CompareSymbols(t1._symbol!, t2._symbol!);
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.StringLiteral) != 0)
        {
            // String literal types are ordered by their values.
            c = string.CompareOrdinal((string)t1.AsLiteralType()!._value!, (string)t2.AsLiteralType()!._value!);
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.NumberLiteral) != 0)
        {
            // Numeric literal types are ordered by their values.
            c = ((Number)t1.AsLiteralType()!._value!).CompareTo((Number)t2.AsLiteralType()!._value!);
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.BigIntLiteral) != 0)
        {
            c = GetBigIntLiteralValue(t1).Compare(GetBigIntLiteralValue(t2));
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.BooleanLiteral) != 0)
        {
            var b1 = (bool)t1.AsLiteralType()!._value!;
            var b2 = (bool)t2.AsLiteralType()!._value!;
            if (b1 != b2)
            {
                return b1 ? 1 : -1;
            }
        }
        else if ((t1._flags & TypeFlags.TypeParameter) != 0)
        {
            c = t1._checker.CompareSymbols(t1._symbol!, t2._symbol!);
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.Index) != 0)
        {
            c = CompareTypes(t1.AsIndexType()!._target, t2.AsIndexType()!._target);
            if (c != 0)
            {
                return c;
            }
            c = (int)t1.AsIndexType()!._indexFlags - (int)t2.AsIndexType()!._indexFlags;
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.IndexedAccess) != 0)
        {
            c = CompareTypes(t1.AsIndexedAccessType()!._objectType, t2.AsIndexedAccessType()!._objectType);
            if (c != 0)
            {
                return c;
            }
            c = CompareTypes(t1.AsIndexedAccessType()!._indexType, t2.AsIndexedAccessType()!._indexType);
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.Conditional) != 0)
        {
            c = t1._checker.CompareNodes(t1.AsConditionalType()!._root!._node!.AsNode(), t2.AsConditionalType()!._root!._node!.AsNode());
            if (c != 0)
            {
                return c;
            }
            c = CompareTypeMappers(t1.AsConditionalType()!._mapper, t2.AsConditionalType()!._mapper);
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.Substitution) != 0)
        {
            c = CompareTypes(t1.AsSubstitutionType()!._baseType, t2.AsSubstitutionType()!._baseType);
            if (c != 0)
            {
                return c;
            }
            c = CompareTypes(t1.AsSubstitutionType()!._constraint, t2.AsSubstitutionType()!._constraint);
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.TemplateLiteral) != 0)
        {
            c = t1.AsTemplateLiteralType()!._texts.AsSpan().SequenceCompareTo(t2.AsTemplateLiteralType()!._texts.AsSpan());
            if (c != 0)
            {
                return c;
            }
            c = CompareTypeLists(t1.AsTemplateLiteralType()!._types, t2.AsTemplateLiteralType()!._types);
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1._flags & TypeFlags.StringMapping) != 0)
        {
            c = CompareTypes(t1.AsStringMappingType()!._target, t2.AsStringMappingType()!._target);
            if (c != 0)
            {
                return c;
            }
        }
        // Fall back to type IDs. This results in type creation order for built-in types.
        return (int)t1._id.Value - (int)t2._id.Value;
    }

    /// <summary>Go: <c>func getSortOrderFlags(t *Type) int</c>（utilities.go:624-630）。</summary>
    internal static int GetSortOrderFlags(Type t)
    {
        // Return TypeFlagsEnum for all enum-like unit types (they'll be sorted by their symbols)
        if ((t._flags & (TypeFlags.EnumLiteral | TypeFlags.Enum)) != 0 && (t._flags & TypeFlags.Union) == 0)
        {
            return (int)TypeFlags.Enum;
        }
        return (int)t._flags;
    }

    /// <summary>Go: <c>func compareTypeNames(t1, t2 *Type) int</c>（utilities.go:632-649）。</summary>
    internal static int CompareTypeNames(Type t1, Type t2)
    {
        var s1 = GetTypeNameSymbol(t1);
        var s2 = GetTypeNameSymbol(t2);
        if (s1 == s2)
        {
            // Go 的 `TypeAlias.TypeArguments()` 带 nil 接收者守卫（types.go:677-682）：`alias == nil` 时返回 nil。
            // C# 没有 nil 接收者调用，`_alias!` 只压警告、运行期仍 NRE，所以必须在调用点用 `?.` 表达守卫。
            return CompareTypeLists(t1._alias?.TypeArguments(), t2._alias?.TypeArguments());
        }
        if (s1 is null)
        {
            return 1;
        }
        if (s2 is null)
        {
            return -1;
        }
        var c = string.CompareOrdinal(s1.Name, s2.Name);
        if (c != 0)
        {
            return c;
        }
        // Keep distinct same-named declarations together before comparing alias arguments or structure.
        return t1._checker.CompareSymbols(s1, s2);
    }

    /// <summary>Go: <c>func getTypeNameSymbol(t *Type) *ast.Symbol</c>（utilities.go:651-659）。</summary>
    internal static Symbol? GetTypeNameSymbol(Type t)
    {
        if (t._alias is not null)
        {
            return t._alias._symbol;
        }
        if ((t._flags & (TypeFlags.TypeParameter | TypeFlags.StringMapping)) != 0 || (t._objectFlags & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
        {
            return t._symbol;
        }
        return null;
    }

    /// <summary>Go: <c>func getObjectTypeName(t *Type) *ast.Symbol</c>（utilities.go:661-666）。</summary>
    internal static Symbol? GetObjectTypeName(Type t)
    {
        if ((t._objectFlags & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
        {
            return t._symbol;
        }
        return null;
    }

    /// <summary>Go: <c>func compareTupleTypes(t1, t2 *TupleType) int</c>（utilities.go:668-689）。</summary>
    internal static int CompareTupleTypes(TupleType t1, TupleType t2)
    {
        if (t1 == t2)
        {
            return 0;
        }
        if (t1._readonly != t2._readonly)
        {
            return IfElse(t1._readonly, 1, -1);
        }
        if (t1._elementInfos!.Length != t2._elementInfos!.Length)
        {
            return t1._elementInfos.Length - t2._elementInfos.Length;
        }
        for (var i = 0; i < t1._elementInfos.Length; i++)
        {
            var c = (int)t1._elementInfos[i]._flags - (int)t2._elementInfos[i]._flags;
            if (c != 0)
            {
                return c;
            }
        }
        for (var i = 0; i < t1._elementInfos.Length; i++)
        {
            var c = CompareElementLabels(t1._elementInfos[i]._labeledDeclaration, t2._elementInfos[i]._labeledDeclaration);
            if (c != 0)
            {
                return c;
            }
        }
        return 0;
    }

    /// <summary>Go: <c>func compareElementLabels(n1, n2 *ast.Node) int</c>（utilities.go:691-702）。</summary>
    internal static int CompareElementLabels(Node? n1, Node? n2)
    {
        if (n1 == n2)
        {
            return 0;
        }
        if (n1 is null)
        {
            return -1;
        }
        if (n2 is null)
        {
            return 1;
        }
        return string.CompareOrdinal(n1.Name()!.Text(), n2.Name()!.Text());
    }

    /// <summary>Go: <c>func compareTypeLists(s1, s2 []*Type) int</c>（utilities.go:704-714）。</summary>
    internal static int CompareTypeLists(Type[]? s1, Type[]? s2)
    {
        var l1 = s1?.Length ?? 0;
        var l2 = s2?.Length ?? 0;
        if (l1 != l2)
        {
            return l1 - l2;
        }
        for (var i = 0; i < l1; i++)
        {
            var c = CompareTypes(s1![i], s2![i]);
            if (c != 0)
            {
                return c;
            }
        }
        return 0;
    }

    /// <summary>Go: <c>func compareTypeMappers(m1, m2 *TypeMapper) int</c>（utilities.go:716-755）。</summary>
    internal static int CompareTypeMappers(TypeMapper? m1, TypeMapper? m2)
    {
        if (m1 == m2)
        {
            return 0;
        }
        if (m1 is null)
        {
            return 1;
        }
        if (m2 is null)
        {
            return -1;
        }
        var kind1 = m1.Kind();
        var kind2 = m2.Kind();
        if (kind1 != kind2)
        {
            return (int)kind1 - (int)kind2;
        }
        switch (kind1)
        {
            case TypeMapperKind.Simple:
            {
                var s1 = (SimpleTypeMapper)m1;
                var s2 = (SimpleTypeMapper)m2;
                var c = CompareTypes(s1._source, s2._source);
                if (c != 0)
                {
                    return c;
                }
                return CompareTypes(s1._target, s2._target);
            }
            case TypeMapperKind.Array:
            {
                var a1 = (ArrayTypeMapper)m1;
                var a2 = (ArrayTypeMapper)m2;
                var c = CompareTypeLists(a1._sources, a2._sources);
                if (c != 0)
                {
                    return c;
                }
                return CompareTypeLists(a1._targets, a2._targets);
            }
            case TypeMapperKind.Merged:
            {
                var g1 = (MergedTypeMapper)m1;
                var g2 = (MergedTypeMapper)m2;
                var c = CompareTypeMappers(g1._m1, g2._m1);
                if (c != 0)
                {
                    return c;
                }
                return CompareTypeMappers(g1._m2, g2._m2);
            }
        }
        return 0;
    }
}
