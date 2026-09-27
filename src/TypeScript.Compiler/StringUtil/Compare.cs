// ported from: internal/stringutil/compare.go
//
// 偏离 Go 的地方（docs/architecture.md §3.4）：
//
// 1) Go 的 `type Comparison = int` 是类型别名（Comparison 就是 int，调用点会做 `c != 0`、`cmp < 0`
//    这类算术/序比较），所以 C# 侧不引入 enum，直接用 int 返回值 + 三个同名常量。用 enum 会让
//    `c != 0`、`cmp < 0` 之类的调用点编译失败（enum 不支持与整数比较）。
// 2) Go 的 func(a, b string) bool / func(a, b string) Comparison 映射为 C# 委托：
//    Func<string, string, bool> 与 System.Comparison<string>（= delegate int (string, string)，
//    可直接传给 List<T>.Sort / Array.Sort）。
// 3) EquateStringCaseInsensitive 用“两边逐码点 Rune.ToLowerInvariant 后序数比较”代替 Go 的
//    strings.EqualFold（SimpleFold 等价类）：.NET 没有 SimpleFold 的等价物，实测
//    StringComparison.OrdinalIgnoreCase 认为 "k" != "\u212A"、"s" != "\u017F"，而 Go 的 EqualFold
//    认为相等；下面的写法比 OrdinalIgnoreCase 更接近 SimpleFold（k 与 U+212A 相等），差异只落在
//    ſ（U+017F）/ẞ（U+1E9E）/词尾 ς 这类等价类边角。
//
// 为热路径避免多余分配：真正的比较逻辑放在 ReadOnlySpan<char> 版本上（Go 的 s[0:len(prefix)] 是零拷贝
// 切片视图，C# 的 Substring 会分配），字符串重载只是把 span 传进去。
using System.Text;

namespace TypeScript.StringUtil;

public static partial class StringUtil
{
    public static bool EquateStringCaseInsensitive(string a, string b)
    {
        // !!!
        // return a == b || strings.ToUpper(a) == strings.ToUpper(b)
        // return strings.EqualFold(a, b)
        return EquateSpansCaseInsensitive(a.AsSpan(), b.AsSpan());
    }

    public static bool EquateStringCaseSensitive(string a, string b)
    {
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    public static Func<string, string, bool> GetStringEqualityComparer(bool ignoreCase)
    {
        if (ignoreCase)
        {
            return EquateStringCaseInsensitive;
        }

        return EquateStringCaseSensitive;
    }

    public const int ComparisonLessThan = -1;

    public const int ComparisonEqual = 0;

    public const int ComparisonGreaterThan = 1;

    public static int CompareStringsCaseInsensitive(string a, string b)
    {
        if (a == b)
        {
            return ComparisonEqual;
        }

        return CompareSpansCaseInsensitive(a.AsSpan(), b.AsSpan());
    }

    public static int CompareStringsCaseSensitive(string a, string b)
    {
        // Go: strings.Compare —— 结果**恒为** -1/0/1（不是差值的任意大小），Comparison 常量也是这么定义的。
        // 直接用 string.CompareOrdinal 会漏出任意差值（例如 "A" vs "a" 得到 -32），而调用点（如 tspath 的
        // ComparePaths 把比较器结果原样返回）会把它当作 Comparison(iota: -1/0/1) 用。所以这里归一化。
        // 注意字典序口径：Go 的字符串比较按 UTF-8 字节序，这里按 UTF-16 代码单元序（docs §3.1），
        // 两者只在 astral 码点与 U+E000–U+FFFF 之间相对次序上可能不同。
        if (a == b)
        {
            return ComparisonEqual;
        }

        return string.CompareOrdinal(a, b) < 0 ? ComparisonLessThan : ComparisonGreaterThan;
    }

    public static Comparison<string> GetStringComparer(bool ignoreCase)
    {
        if (ignoreCase)
        {
            return CompareStringsCaseInsensitive;
        }

        return CompareStringsCaseSensitive;
    }

    public static bool HasPrefix(string s, string prefix, bool caseSensitive)
    {
        if (caseSensitive)
        {
            return s.StartsWith(prefix, StringComparison.Ordinal);
        }

        if (prefix.Length > s.Length)
        {
            return false;
        }

        return EquateSpansCaseInsensitive(s.AsSpan(0, prefix.Length), prefix.AsSpan());
    }

    public static bool HasSuffix(string s, string suffix, bool caseSensitive)
    {
        if (caseSensitive)
        {
            return s.EndsWith(suffix, StringComparison.Ordinal);
        }

        if (suffix.Length > s.Length)
        {
            return false;
        }

        return EquateSpansCaseInsensitive(s.AsSpan(s.Length - suffix.Length), suffix.AsSpan());
    }

    public static bool HasPrefixAndSuffixWithoutOverlap(string s, string prefix, string suffix, bool caseSensitive)
    {
        if (prefix.Length + suffix.Length > s.Length)
        {
            return false;
        }

        return HasPrefix(s, prefix, caseSensitive) && HasSuffix(s, suffix, caseSensitive);
    }

    public static int CompareStringsCaseInsensitiveThenSensitive(string a, string b)
    {
        var cmp = CompareStringsCaseInsensitive(a, b);
        if (cmp != ComparisonEqual)
        {
            return cmp;
        }

        return CompareStringsCaseSensitive(a, b);
    }

    // CompareStringsCaseInsensitiveEslintCompatible performs a case-insensitive comparison
    // using toLowerCase() instead of toUpperCase() for ESLint compatibility.
    //
    // `CompareStringsCaseInsensitive` transforms letters to uppercase for unicode reasons,
    // while eslint's `sort-imports` rule transforms letters to lowercase. Which one you choose
    // affects the relative order of letters and ASCII characters 91-96, of which `_` is a
    // valid character in an identifier. So if we used `CompareStringsCaseInsensitive` for
    // import sorting, TypeScript and eslint would disagree about the correct case-insensitive
    // sort order for `__String` and `Foo`. Since eslint's whole job is to create consistency
    // by enforcing nitpicky details like this, it makes way more sense for us to just adopt
    // their convention so users can have auto-imports without making eslint angry.
    public static int CompareStringsCaseInsensitiveEslintCompatible(string a, string b)
    {
        if (a == b)
        {
            return ComparisonEqual;
        }

        // Go: strings.ToLower —— 完整的 JS 语义小写映射，对应 ToLowerJS（docs §3.4）。
        a = ToLowerJS(a);
        b = ToLowerJS(b);
        // Go 用的是 strings.Compare，所以同样必须归一化成 -1/0/1（见 CompareStringsCaseSensitive）。
        return CompareStringsCaseSensitive(a, b);
    }

    // Go: CompareStringsCaseInsensitive，但直接在 span 上走，避免 HasPrefix/HasSuffix 为了切前缀而分配子串。
    // 逐码点解码的口径与 Go 的 utf8.DecodeRuneInString 一致：非法序列（含孤立代理项）按 U+FFFD、消耗 1 个单元。
    private static int CompareSpansCaseInsensitive(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var ia = 0;
        var ib = 0;
        while (true)
        {
            if (ia == a.Length)
            {
                if (ib == b.Length)
                {
                    return ComparisonEqual;
                }

                return ComparisonLessThan;
            }

            if (ib == b.Length)
            {
                return ComparisonGreaterThan;
            }

            var lca = Rune.ToLowerInvariant(DecodeRuneAt(a, ia, out var sizeA));
            var lcb = Rune.ToLowerInvariant(DecodeRuneAt(b, ib, out var sizeB));
            if (lca.Value != lcb.Value)
            {
                if (lca.Value < lcb.Value)
                {
                    return ComparisonLessThan;
                }

                return ComparisonGreaterThan;
            }

            ia += sizeA;
            ib += sizeB;
        }
    }

    private static bool EquateSpansCaseInsensitive(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        return CompareSpansCaseInsensitive(a, b) == ComparisonEqual;
    }

    // 同 Util.cs 的 DecodeRuneAt，只是作用在 span 上（比较函数全程不碰 string 切片）。
    private static Rune DecodeRuneAt(ReadOnlySpan<char> span, int index, out int size)
    {
        _ = Rune.DecodeFromUtf16(span[index..], out var value, out size);
        return value;
    }
}
