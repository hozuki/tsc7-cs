// ported from: internal/jsnum/pseudobigint_test.go
//
// 移植映射：Go 的三个子测试（"strip base-10 strings"、"parse non-decimal bases (small numbers)"、
// "can parse large literals"）→ 三个测试方法。
//
// C# 侧新增（Go 的测试文件没覆盖，但这些都是外部包会用到的公开 API）：
//   * Sign()/String()/NewPseudoBigInt()/ParseValidBigInt()：checker 用 ParseValidBigInt/NewPseudoBigInt
//     建 bigint 字面量类型（internal/checker/checker.go:7923、internal/ls/utilities.go:1089）。
//   * Compare()：checker 用它判断两个 bigint 字面量类型是否同一（internal/checker/utilities.go:564）。
//   * `==` / `default(PseudoBigInt)`：evaluator.IsTruthy 用 `v != jsnum.PseudoBigInt{}` 做真值判断
//     （internal/evaluator/evaluator.go:167），C# 侧对应 default(PseudoBigInt)。
using Xunit;
using Xunit.Abstractions;
using TypeScript.JsNum;
using static TypeScript.JsNum.JsNum;

namespace TypeScript.Tests;

public class PseudoBigIntTests(ITestOutputHelper output)
{
    [Fact]
    public void TestParsePseudoBigIntStripBase10Strings()
    {
        var testNumbers = new List<Number>();
        for (var i = 0; i < 1000; i++)
        {
            testNumbers.Add((Number)i);
        }

        for (var bits = 0; bits < 53; bits++)
        {
            testNumbers.Add((Number)(1L << bits));
            testNumbers.Add((Number)((1L << bits) - 1));
        }

        foreach (var testNumber in testNumbers)
        {
            for (var leadingZeros = 0; leadingZeros < 10; leadingZeros++)
            {
                Assert.Equal(
                    testNumber.String(),
                    ParsePseudoBigInt(new string('0', leadingZeros) + testNumber.String() + "n"));
            }
        }
    }

    public static TheoryData<string, string> NonDecimalBaseCases => new()
    {
        // binary
        { "0b0n", "0" },
        { "0b1n", "1" },
        { "0b1010n", "10" },
        { "0b1010_0101n", "165" },
        { "0B1101n", "13" }, // uppercase prefix

        // octal
        { "0o0n", "0" },
        { "0o7n", "7" },
        { "0o755n", "493" },
        { "0o7_5_5n", "493" },
        { "0O12n", "10" }, // uppercase prefix

        // hex
        { "0x0n", "0" },
        { "0xFn", "15" },
        { "0xFFn", "255" },
        { "0xF_Fn", "255" },
        { "0X1Fn", "31" }, // uppercase prefix
    };

    [Theory]
    [MemberData(nameof(NonDecimalBaseCases))]
    public void TestParsePseudoBigIntNonDecimalBases(string lit, string expected)
    {
        output.WriteLine($"literal: {lit}");
        Assert.Equal(expected, ParsePseudoBigInt(lit));
    }

    [Theory]
    [InlineData("123456789012345678901234567890n", "123456789012345678901234567890")]
    [InlineData(
        "0b1100011101110100100001111111101101100001101110011111000001110111001001110001111110000101011010010n",
        "123456789012345678901234567890")]
    [InlineData("0o143564417755415637016711617605322n", "123456789012345678901234567890")]
    [InlineData("0x18ee90ff6c373e0ee4e3f0ad2n", "123456789012345678901234567890")]
    public void TestParsePseudoBigIntLargeLiterals(string lit, string expected)
    {
        output.WriteLine($"literal: {lit}");
        Assert.Equal(expected, ParsePseudoBigInt(lit));
    }

    [Theory]
    [InlineData("0", 0, "0")]
    [InlineData("-0", 0, "0")]
    [InlineData("123", 1, "123")]
    [InlineData("-123", -1, "-123")]
    [InlineData("123n", 1, "123")]
    public void TestPseudoBigIntSignAndString(string text, int sign, string str)
    {
        var value = ParseValidBigInt(text);
        Assert.Equal(sign, value.Sign());
        Assert.Equal(str, value.String());
    }

    [Fact]
    public void TestPseudoBigIntCompare()
    {
        // Go 的 cmp.Compare + PseudoBigInt.Compare 语义：先比符号、再比长度、再比字典序，
        // 负数整体取反。返回值与 Go 一样只有 -1/0/1。
        Assert.Equal(0, ParseValidBigInt("0").Compare(default(PseudoBigInt)));
        Assert.Equal(0, ParseValidBigInt("0").Compare(ParseValidBigInt("-0")));
        Assert.Equal(0, ParseValidBigInt("123").Compare(ParseValidBigInt("123")));
        Assert.Equal(1, ParseValidBigInt("1").Compare(ParseValidBigInt("0")));
        Assert.Equal(-1, ParseValidBigInt("0").Compare(ParseValidBigInt("1")));
        Assert.Equal(-1, ParseValidBigInt("-1").Compare(ParseValidBigInt("0")));
        Assert.Equal(1, ParseValidBigInt("0").Compare(ParseValidBigInt("-1")));
        Assert.Equal(-1, ParseValidBigInt("1").Compare(ParseValidBigInt("2")));
        Assert.Equal(1, ParseValidBigInt("2").Compare(ParseValidBigInt("1")));
        Assert.Equal(1, ParseValidBigInt("10").Compare(ParseValidBigInt("9")));
        Assert.Equal(-1, ParseValidBigInt("-10").Compare(ParseValidBigInt("-9")));
        Assert.Equal(1, ParseValidBigInt("-9").Compare(ParseValidBigInt("-10")));
        Assert.Equal(-1, ParseValidBigInt("-1").Compare(ParseValidBigInt("1")));
        Assert.Equal(1, ParseValidBigInt("1").Compare(ParseValidBigInt("-1")));
    }

    [Fact]
    public void TestPseudoBigIntZeroState()
    {
        // evaluator.IsTruthy 的 `v != jsnum.PseudoBigInt{}`。
        var zero = default(PseudoBigInt);
        Assert.False(zero.Negative);
        Assert.Equal("", zero.Base10Value);
        Assert.Equal(0, zero.Sign());
        Assert.Equal("0", zero.String());
        Assert.True(ParseValidBigInt("0") == zero);
        Assert.False(ParseValidBigInt("1") == zero);
        Assert.False(ParseValidBigInt("-1") == zero);
        Assert.True(NewPseudoBigInt("000", false) == zero);
        Assert.Equal("123", NewPseudoBigInt("00123", false).Base10Value);
    }
}
