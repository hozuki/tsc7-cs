// ported from: internal/stringutil/js_case_test.go
//
// 1) Go 的 `t.Run(name, ...)` 子测试 + 表驱动 → xunit 的 [Theory] + [InlineData]；用例名保留为第一个
//    参数并打进测试输出（xunit 2.9.3 的 InlineData 没有 DisplayName 属性）。
// 2) Go 表里 got/want 是**表达式**（`ToLowerJS(...)` / `EncodeJSStringRune(0xD800)` 等），而 InlineData
//    只接受编译期常量，所以这里把表拆成"函数 + 输入 + 后缀 + 期望"四列，期望值一个都没改：
//      got = ToLowerJS/ToUpperJS(input) + suffix
//    （"capitalize-style uppercase" 与 "uncapitalize-style lowercase" 这两条 Go 用例本身就是
//      `ToUpperJS("ß") + "foo"` 这种写法，于是后缀列正好承载它。）
// 3) Go 的 `EncodeJSStringRune(0xD800)` 是把孤立代理项编成 CESU-8 哨兵（Go 的 string 是 UTF-8，孤立代理项
//    无法直接表示）。C# 的 string 是 UTF-16 代码单元序列，孤立代理项天生合法，等价的写法就是字面量
//    "\uD800" / "\uDC00"（与 docs/architecture.md §3.1 一致）。
// 4) 非 ASCII 字符用 \uXXXX 转义写出（值与原 Go 字面量逐字符相同），行尾注释给出可读形式。
using Xunit;
using Xunit.Abstractions;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Tests;

public class StringUtilJsCaseTests(ITestOutputHelper output)
{
    [Theory]
    // 1
    [InlineData("ascii lowercase", false, "HELLO", "", "hello")]
    // 2
    [InlineData("ascii uppercase", true, "hello", "", "HELLO")]
    // 3
    [InlineData("lowercase dotted i", false, "\u0130SPANYOL", "", "i\u0307spanyol")] // İSPANYOL → i̇spanyol
    // 4
    [InlineData("lowercase lone sigma", false, "\u03A3", "", "\u03C3")] // Σ → σ
    // 5
    [InlineData("lowercase final sigma", false, "\u039F\u03A3", "", "\u03BF\u03C2")] // ΟΣ → ος
    // 6
    [InlineData("lowercase non-sigma greek", false, "\u03A9", "", "\u03C9")] // Ω → ω
    // 7
    [InlineData("uppercase sharp s", true, "\u00DFfoo", "", "SSFOO")] // ßfoo → SSFOO
    // 8
    [InlineData("uppercase non-ascii simple mapping", true, "\u03C9", "", "\u03A9")] // ω → Ω
    // 9
    [InlineData("uppercase ligature", true, "\uFB01oo", "", "FIOO")] // ﬁoo → FIOO
    // 10
    [InlineData("capitalize-style uppercase", true, "\u00DF", "foo", "SSfoo")] // ToUpperJS("ß") + "foo"
    // 11
    [InlineData("uncapitalize-style lowercase", false, "\u0130", "foo", "i\u0307foo")] // ToLowerJS("İ") + "foo"
    // 12
    [InlineData(
        "lowercase final sigma after lowercase letter without uppercase mapping",
        false,
        "\u0295\u03A3", // ʕΣ
        "",
        "\u0295\u03C2")] // ʕς
    // 13
    [InlineData("lowercase sigma after modifier letter", false, "\u02B0\u03A3", "", "\u02B0\u03C3")] // ʰΣ → ʰσ
    // 14
    [InlineData(
        "lowercase sigma after case ignorable ypogegrammeni",
        false,
        "\u0345\u03A3", // ͅΣ
        "",
        "\u0345\u03C3")] // ͅσ
    // 15
    [InlineData(
        "lowercase final sigma after feminine ordinal indicator",
        false,
        "\u00AA\u03A3", // ªΣ
        "",
        "\u00AA\u03C2")] // ªς
    // 16
    [InlineData(
        "lowercase final sigma after masculine ordinal indicator",
        false,
        "\u00BA\u03A3", // ºΣ
        "",
        "\u00BA\u03C2")] // ºς
    // 17
    [InlineData("lowercase final sigma after roman numeral", false, "\u2160\u03A3", "", "\u2170\u03C2")] // ⅠΣ → ⅰς
    // 18
    [InlineData("lowercase sigma after uppercase property added after unicode 15", false, "\u1C89\u03A3", "", "\u1C89\u03C3")]
    // 19
    [InlineData("lowercase sigma after uppercase property skewed from local v8 unicode data", false, "\uA7CB\u03A3", "", "\uA7CB\u03C3")]
    // 20
    [InlineData("lowercase sigma before immediate latin letter", false, "\u03A3A", "", "\u03C3a")] // ΣA → σa
    // 21
    [InlineData("lowercase sigma before immediate roman numeral letter", false, "\u03A3\u2160", "", "\u03C3\u2170")] // ΣⅠ → σⅰ
    // 22
    [InlineData("lowercase sigma before case ignorable then latin letter", false, "\u03A3\u0345A", "", "\u03C3\u0345a")] // ΣͅA → σͅa
    // 23
    [InlineData("uppercase lone surrogate", true, "\uD800", "", "\uD800")]
    // 24
    [InlineData("lowercase lone surrogate", false, "A\uD800B", "", "a\uD800b")]
    // 25
    [InlineData("uppercase lone low surrogate with text", true, "\uDC00x", "", "\uDC00X")]
    // 26
    [InlineData("lowercase lone surrogate before sigma", false, "\uD800\u03A3", "", "\uD800\u03C3")]
    public void TestJSCasing(string name, bool toUpper, string input, string suffix, string expected)
    {
        output.WriteLine(name);
        var got = (toUpper ? ToUpperJS(input) : ToLowerJS(input)) + suffix;
        Assert.Equal(expected, got);
    }
}
