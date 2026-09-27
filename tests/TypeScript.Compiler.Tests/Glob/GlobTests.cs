// glob 包的测试（Go 侧 internal/glob **没有** glob_test.go：它只被 tsoptions 间接使用）。
// 本文件是新增测试（Main 要求 glob.go 有对应测试），期望值全部由 glob.go 的实现语义推出
// （LSP glob 语法注释 + parse/match 的逐行行为），并按 TSGO 的既有行为断言：
//   * `*` 在**一个** path segment 内匹配（不跨 '/'）；
//   * `?` 匹配 1 个代码单元且不匹配 '/'；
//   * `**` 只允许紧邻 '/'，可匹配零个或多个 segment（`**/a` 匹配 "a"）；
//   * `{a,b}` 分组、`[a-c]` 字符区间；
//   * `slash` 元素匹配一个或多个连续 '/'。
// 已知的 Go 端行为（照抄，不"顺手修"）：`charRange` 的 `negate` 在 `match` 里从未被读取，
// 所以 `[!a-c]` 与 `[a-c]` 的匹配行为相同（`[!a-c]` 仍然匹配 'b'、不匹配 'd'）。
using System.Collections.Generic;
using TypeScript.Glob;
using Xunit;

// `Glob` 既是命名空间又是类型（docs §2 的遮蔽规则）→ 用别名消歧
using GlobClass = TypeScript.Glob.Glob;

namespace TypeScript.Tests;

public class GlobTests
{
    private static GlobClass Parse(string pattern)
    {
        Assert.True(GlobClass.TryParse(pattern, out var glob), $"pattern '{pattern}' 应当能解析");
        return glob!;
    }

    [Theory]
    // `*` 在一个 segment 内匹配
    [InlineData("*.ts", "a.ts", true)]
    [InlineData("*.ts", "a.js", false)]
    [InlineData("*", "a", true)]
    [InlineData("*", "a/b", false)]
    // `?` 匹配一个字符（或代码单元），不匹配 '/'
    [InlineData("?", "a", true)]
    [InlineData("?", "ab", false)]
    [InlineData("?", "/", false)]
    [InlineData("a?c", "abc", true)]
    // `**` 可匹配零个或多个 segment
    [InlineData("**/a", "a", true)]
    [InlineData("**/a", "x/a", true)]
    [InlineData("**/a", "x/y/a", true)]
    [InlineData("**/*.ts", "c.ts", true)]
    [InlineData("**/*.ts", "/a/b/c.ts", true)]
    [InlineData("**/*.ts", "/a/b/c.js", false)]
    // 分组
    [InlineData("{a,b}.ts", "a.ts", true)]
    [InlineData("{a,b}.ts", "b.ts", true)]
    [InlineData("{a,b}.ts", "c.ts", false)]
    [InlineData("**/*.{ts,js}", "/x/y/z.js", true)]
    // 字符区间
    [InlineData("[a-c].ts", "b.ts", true)]
    [InlineData("[a-c].ts", "d.ts", false)]
    // `slash` 元素匹配一个或多个连续 '/'
    [InlineData("/a", "//a", true)]
    [InlineData("/a", "a", false)]
    // 普通字面量
    [InlineData("/a/b.ts", "/a/b.ts", true)]
    [InlineData("/a/b.ts", "/a/c.ts", false)]
    // 尾部 `*` 吃掉整个 segment
    [InlineData("a*", "abc", true)]
    [InlineData("a*", "a/b", false)]
    public void Match(string pattern, string input, bool expected)
    {
        Assert.Equal(expected, Parse(pattern).Match(input));
    }

    [Theory]
    [InlineData("**a")] // `**` 必须紧邻 '/'
    [InlineData("a/**b")]
    [InlineData("[")] // 空区间
    [InlineData("[]")] // 区间不是 [x-y] 形式
    [InlineData("[a-")] // 缺少 ']'
    [InlineData("{")] // 未闭合的 '{'
    [InlineData("{a")] // 组内子模式吃不到 '}'
    public void ParseRejectsInvalidPatterns(string pattern)
    {
        Assert.False(GlobClass.TryParse(pattern, out _), $"pattern '{pattern}' 应当解析失败");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("**/*.ts")]
    [InlineData("{a,b}")]
    [InlineData("[a-c]")]
    [InlineData("a/b/c")]
    [InlineData("a/b/c/d/")]
    public void ToStringRoundTrips(string pattern)
    {
        // Go: Glob.String()（fmt.Stringer）逐元素还原模式文本。
        Assert.Equal(pattern, Parse(pattern).ToString());
    }

    [Fact]
    public void MatchUnicodeCharRangeUsesCodePoints()
    {
        // §3.1：`[` 区间解码按码点（Go 的 utf8.DecodeRuneInString ↔ C# Rune.DecodeFromUtf16）。
        Assert.True(Parse("[α-ω].ts").Match("β.ts"));
        Assert.False(Parse("[α-ω].ts").Match("z.ts"));
    }

    [Fact]
    public void MatchIsDeterministicForRepeatedCalls()
    {
        // 同一 Glob 反复 Match 不得改变内部状态（Go 的 match 只读 elems）。
        var glob = Parse("**/*.{ts,tsx}");
        var results = new List<bool>();
        for (var i = 0; i < 3; i++)
        {
            results.Add(glob.Match("/a/b/c.tsx"));
        }
        Assert.Equal([true, true, true], results);
    }
}
