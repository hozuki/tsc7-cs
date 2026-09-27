// 新增测试（Go 里没有对应文件）：把 docs/architecture.md §3.4 依赖的 .NET Unicode 数据假设、以及 Go
// 比较器的返回值契约钉成哨兵。
//
// 依据 artifacts/unicode-probe（`dotnet run Program.cs`）实测的 .NET 简单大小写映射。这些假设一旦被
// .NET 升级 Unicode 数据（例如升到 Unicode 16）悄悄破坏，StringUtil 的路径规范化与大小写比较语义就会
// 跟着改变，所以这里显式断言；失败时应当重新评估 docs §3.4 的 Go↔C# 映射表，而不是改这些期望值。
// 参考：Go 的 unicode.ToLower/ToUpper 是 simple 映射 → Rune.ToLowerInvariant/ToUpperInvariant；
// strings.EqualFold 是 SimpleFold 等价类 → 我们用"两边逐码点 ToLowerInvariant 后序数比较"。
//
// 比较器契约：Go 的 strings.Compare 恒返回 -1/0/1（不是任意差值），而 Comparison 就是 int、会被调用点
// 原样返回（例如 tspath 的 ComparePaths）。C# 的 string.CompareOrdinal 返回任意差值，所以必须归一化，
// 下面最后一个 [Fact] 就是防这件事回退的。
using System.Text;
using Xunit;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Tests;

public class StringUtilCaseMappingAssumptionsTests
{
    // docs §3.4：Rune.ToLowerInvariant(0x212A) == 0x6B（KELVIN SIGN 的小写是 ascii k）。
    [Fact]
    public void KelvinSignLowersToAsciiK()
        => Assert.Equal(0x6B, Rune.ToLowerInvariant(new Rune(0x212A)).Value);

    // docs §3.4：Rune.ToUpperInvariant(0x017F) == 'S'（LATIN SMALL LETTER LONG S 的大写是 S）。
    [Fact]
    public void LongSUpperCasesToAsciiS()
        => Assert.Equal('S', (char)Rune.ToUpperInvariant(new Rune(0x017F)).Value);

    // docs §3.4：Rune.ToLowerInvariant(0x1E9E) == 0xDF（LATIN CAPITAL LETTER SHARP S 的小写是 ß，单字符）。
    [Fact]
    public void CapitalSharpSLowersToSharpS()
        => Assert.Equal(0xDF, Rune.ToLowerInvariant(new Rune(0x1E9E)).Value);

    // docs §3.4：Rune.ToLowerInvariant(0x0130) == 0x0130（LATIN CAPITAL LETTER I WITH DOT ABOVE 没有 simple
    // 小写映射；JS 的完整映射 İ→i̇ 由生成表 SpecialCasingMappings 提供，不走 Rune）。
    [Fact]
    public void DottedCapitalIHasNoSimpleLowerMapping()
        => Assert.Equal(0x0130, Rune.ToLowerInvariant(new Rune(0x0130)).Value);

    // docs §3.4：.NET 15.1 数据里 U+1C89 / U+A7CB（Unicode 16 新增的大写字母）还没有映射，
    // 与 TSGO 钉住的 Unicode 15.1 一致（JsCaseTests 里第 18/19 条用例依赖这一点）。
    [Fact]
    public void Unicode16UpperCasesHaveNoMappingYet()
    {
        Assert.Equal(0x1C89, Rune.ToLowerInvariant(new Rune(0x1C89)).Value);
        Assert.Equal(0xA7CB, Rune.ToLowerInvariant(new Rune(0xA7CB)).Value);
    }

    // 由上面第一条假设推出的比较语义：CompareStringsCaseInsensitive 逐码点 ToLowerInvariant 后比较，
    // 所以 "k" 与 U+212A 相等 —— 这正是选它而不是 StringComparison.OrdinalIgnoreCase 的理由
    // （OrdinalIgnoreCase 实测认为两者不等，docs §3.4）。
    [Fact]
    public void CaseInsensitiveCompareFoldsKelvinSignToK()
    {
        Assert.Equal(ComparisonEqual, CompareStringsCaseInsensitive("k", "\u212A"));
        Assert.Equal(ComparisonEqual, CompareStringsCaseInsensitive("K", "\u212A"));
    }

    // 已知的与 Go strings.EqualFold 的差异（docs §3.4）：ſ（U+017F）与 s 在 SimpleFold 下相等，
    // 但两边取 ToLowerInvariant 后是 U+017F 与 's'，序数比较不相等。这里把差异钉住，
    // 避免有人误以为 EquateStringCaseInsensitive 完全等价于 Go 的 EqualFold。
    [Fact]
    public void LongSIsNotFoldedToAsciiS()
        => Assert.NotEqual(ComparisonEqual, CompareStringsCaseInsensitive("s", "\u017F"));

    // Go 的 strings.Compare 恒返回 -1/0/1；Comparison 是 int 别名，会被调用点原样转发
    // （tspath.ComparePaths 就 return 比较器结果）。C# 的 string.CompareOrdinal 返回任意差值
    //（"A" vs "a" 是 -32、"A" vs "z" 是 -57），所以这两个比较器必须归一化。
    [Fact]
    public void ComparisonResultsAreAlwaysMinusOneZeroOrOne()
    {
        Assert.Equal(ComparisonLessThan, CompareStringsCaseSensitive("A", "a"));
        Assert.Equal(ComparisonGreaterThan, CompareStringsCaseSensitive("a", "A"));
        Assert.Equal(ComparisonLessThan, CompareStringsCaseSensitive("A", "z"));
        Assert.Equal(ComparisonLessThan, CompareStringsCaseInsensitiveEslintCompatible("A", "z"));
        // cmp == 0 后的收尾比较也是 strings.Compare：'a' > 'A'。
        Assert.Equal(ComparisonGreaterThan, CompareStringsCaseInsensitiveThenSensitive("a", "A"));
    }
}
