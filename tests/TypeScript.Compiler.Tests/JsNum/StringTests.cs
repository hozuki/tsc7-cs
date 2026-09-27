// ported from: internal/jsnum/string_test.go + internal/jsnum/ryu_test.go（ryu 向量合并进这里，
// 与 Go 的 `var stringTests = slices.Concat([]stringTest{...}, ryuTests)` 一致）
//
// 移植映射：
//   * Go 的 `stringTest` 结构体 → (double Number, string Str) 元组；`stringTests` → 私有数组 +
//     由它生成的 TheoryData（Node 交叉验证需要原始数组，[MemberData] 需要 TheoryData，两边共用同一份数据）。
//   * Go 的 `ieeeParts2Double`（ryu_test.go 的 helper，越界时 panic）→ IeeeParts2Double（panic → Debug.Fail）。
//   * TestStringJS / FuzzStringJS / FuzzFromStringJS 需要 Node.js（Go 里是 jstest.EvalNodeScript +
//     SkipIfNoNodeJS），C# 侧走 JsTest.cs。
//   * Go 的 fuzz 只有在 `go test -fuzz` 时才用真 fuzz；普通 `go test` 只跑种子语料，所以这里按种子语料
//     逐条断言（一次 Node 调用处理全部种子——断言与 Go 逐条调用完全一样，只是少了 N 次进程启动）。
//   * Go 的 ryu 向量里 `0o` 开头的八进制字面量 C# 没有对应物，写成等值十进制并注明原字面量。
using Xunit;
using Xunit.Abstractions;
using TypeScript.JsNum;
using static TypeScript.JsNum.JsNum;

namespace TypeScript.Tests;

public class StringTests(ITestOutputHelper output)
{
    private const ulong _maxMantissa = (1UL << 53) - 1;

    // Go: ryu_test.go 的 ieeeParts2Double（若入参越界则 panic → Debug.Fail）。
    private static double IeeeParts2Double(bool sign, uint ieeeExponent, ulong ieeeMantissa)
    {
        if (ieeeExponent > 2047)
        {
            Debug.Fail("ieeeExponent > 2047");
        }

        if (ieeeMantissa > _maxMantissa)
        {
            Debug.Fail("ieeeMantissa > maxMantissa");
        }

        var signBit = sign ? 1UL : 0UL;
        return BitConverter.Int64BitsToDouble(unchecked((long)((signBit << 63) | ((ulong)ieeeExponent << 52) | ieeeMantissa)));
    }

    private static double FromBits(ulong bits)
    {
        return (double)JsTest.NumberFromBits(bits);
    }

    // Go: jsnum_test.go 的 negativeZero（package 私有变量）。
    private static readonly double _negativeZeroValue = (double)_negativeZero;

    private static readonly (double Number, string Str)[] _stringTests =
    [
        (double.NaN, "NaN"),
        (double.PositiveInfinity, "Infinity"),
        (double.NegativeInfinity, "-Infinity"),
        (0, "0"),
        (_negativeZeroValue, "0"),
        (1, "1"),
        (-1, "-1"),
        (0.3, "0.3"),
        (-0.3, "-0.3"),
        (1.5, "1.5"),
        (-1.5, "-1.5"),
        (1e308, "1e+308"),
        (-1e308, "-1e+308"),
        (Math.PI, "3.141592653589793"),
        (-Math.PI, "-3.141592653589793"),
        ((double)MaxSafeInteger, "9007199254740991"),
        ((double)MinSafeInteger, "-9007199254740991"),
        (FromBits(0x000FFFFFFFFFFFFFUL), "2.225073858507201e-308"),
        (FromBits(0x0010000000000000UL), "2.2250738585072014e-308"),
        (1234567.8, "1234567.8"),
        (19686109595169230000d, "19686109595169230000"),
        (123.456, "123.456"),
        (-123.456, "-123.456"),
        (444123, "444123"),
        (-444123, "-444123"),
        (444123.789123456789875436, "444123.7891234568"),
        (-444123.78963636363636363636, "-444123.7896363636"),
        (1e21, "1e+21"),
        (1e20, "100000000000000000000"),
        // ryu_test.go（Copied from https://github.com/ulfjack/ryu/blob/1264a946ba66eab320e927bfd2362e0c8580c42f/ryu/tests/d2s_test.cc
        // Modified to fit Number::toString's output.）
        (2.2250738585072014e-308, "2.2250738585072014e-308"),
        (FromBits(0x7fefffffffffffffUL), "1.7976931348623157e+308"),
        (FromBits(1), "5e-324"),
        (2.98023223876953125e-8, "2.9802322387695312e-8"),
        (-2.109808898695963e16, "-21098088986959630"),
        (4.940656e-318, "4.940656e-318"),
        (1.18575755e-316, "1.18575755e-316"),
        (2.989102097996e-312, "2.989102097996e-312"),
        (9.0608011534336e15, "9060801153433600"),
        (4.708356024711512e18, "4708356024711512000"),
        (9.409340012568248e18, "9409340012568248000"),
        (1.2345678, "1.2345678"),
        (FromBits(0x4830F0CF064DD592UL), "5.764607523034235e+39"),
        (FromBits(0x4840F0CF064DD592UL), "1.152921504606847e+40"),
        (FromBits(0x4850F0CF064DD592UL), "2.305843009213694e+40"),
        (1.2, "1.2"),
        (1.23, "1.23"),
        (1.234, "1.234"),
        (1.2345, "1.2345"),
        (1.23456, "1.23456"),
        (1.234567, "1.234567"),
        (1.2345678, "1.2345678"),
        (1.23456789, "1.23456789"),
        (1.234567895, "1.234567895"),
        (1.2345678901, "1.2345678901"),
        (1.23456789012, "1.23456789012"),
        (1.234567890123, "1.234567890123"),
        (1.2345678901234, "1.2345678901234"),
        (1.23456789012345, "1.23456789012345"),
        (1.234567890123456, "1.234567890123456"),
        (1.2345678901234567, "1.2345678901234567"),
        (4.294967294, "4.294967294"),
        (4.294967295, "4.294967295"),
        (4.294967296, "4.294967296"),
        (4.294967297, "4.294967297"),
        (4.294967298, "4.294967298"),
        (IeeeParts2Double(false, 4, 0), "1.7800590868057611e-307"),
        (IeeeParts2Double(false, 6, _maxMantissa), "2.8480945388892175e-306"),
        (IeeeParts2Double(false, 41, 0), "2.446494580089078e-296"),
        (IeeeParts2Double(false, 40, _maxMantissa), "4.8929891601781557e-296"),
        (IeeeParts2Double(false, 1077, 0), "18014398509481984"),
        (IeeeParts2Double(false, 1076, _maxMantissa), "36028797018963964"),
        (IeeeParts2Double(false, 307, 0), "2.900835519859558e-216"),
        (IeeeParts2Double(false, 306, _maxMantissa), "5.801671039719115e-216"),
        (IeeeParts2Double(false, 934, 0x000FA7161A4D6E0CUL), "3.196104012172126e-27"),
        (9007199254740991.0, "9007199254740991"),
        (9007199254740992.0, "9007199254740992"),
        (1.0e+0, "1"),
        (1.2e+1, "12"),
        (1.23e+2, "123"),
        (1.234e+3, "1234"),
        (1.2345e+4, "12345"),
        (1.23456e+5, "123456"),
        (1.234567e+6, "1234567"),
        (1.2345678e+7, "12345678"),
        (1.23456789e+8, "123456789"),
        (1.23456789e+9, "1234567890"),
        (1.234567895e+9, "1234567895"),
        (1.2345678901e+10, "12345678901"),
        (1.23456789012e+11, "123456789012"),
        (1.234567890123e+12, "1234567890123"),
        (1.2345678901234e+13, "12345678901234"),
        (1.23456789012345e+14, "123456789012345"),
        (1.234567890123456e+15, "1234567890123456"),
        (1.0e+0, "1"),
        (1.0e+1, "10"),
        (1.0e+2, "100"),
        (1.0e+3, "1000"),
        (1.0e+4, "10000"),
        (1.0e+5, "100000"),
        (1.0e+6, "1000000"),
        (1.0e+7, "10000000"),
        (1.0e+8, "100000000"),
        (1.0e+9, "1000000000"),
        (1.0e+10, "10000000000"),
        (1.0e+11, "100000000000"),
        (1.0e+12, "1000000000000"),
        (1.0e+13, "10000000000000"),
        (1.0e+14, "100000000000000"),
        (1.0e+15, "1000000000000000"),
        (1000000000000001, "1000000000000001"),
        (1000000000000010, "1000000000000010"),
        (1000000000000100, "1000000000000100"),
        (1000000000001000, "1000000000001000"),
        (1000000000010000, "1000000000010000"),
        (1000000000100000, "1000000000100000"),
        (1000000001000000, "1000000001000000"),
        (1000000010000000, "1000000010000000"),
        (1000000100000000, "1000000100000000"),
        (1000001000000000, "1000001000000000"),
        (1000010000000000, "1000010000000000"),
        (1000100000000000, "1000100000000000"),
        (1001000000000000, "1001000000000000"),
        (1010000000000000, "1010000000000000"),
        (1100000000000000, "1100000000000000"),
        (8.0, "8"),
        (64.0, "64"),
        (512.0, "512"),
        (8192.0, "8192"),
        (65536.0, "65536"),
        (524288.0, "524288"),
        (8388608.0, "8388608"),
        (67108864.0, "67108864"),
        (536870912.0, "536870912"),
        (8589934592.0, "8589934592"),
        (68719476736.0, "68719476736"),
        (549755813888.0, "549755813888"),
        (8796093022208.0, "8796093022208"),
        (70368744177664.0, "70368744177664"),
        (562949953421312.0, "562949953421312"),
        (9007199254740992.0, "9007199254740992"),
        (8.0e+3, "8000"),
        (64.0e+3, "64000"),
        (512.0e+3, "512000"),
        (8192.0e+3, "8192000"),
        (65536.0e+3, "65536000"),
        (524288.0e+3, "524288000"),
        (8388608.0e+3, "8388608000"),
        (67108864.0e+3, "67108864000"),
        (536870912.0e+3, "536870912000"),
        (8589934592.0e+3, "8589934592000"),
        (68719476736.0e+3, "68719476736000"),
        (549755813888.0e+3, "549755813888000"),
        (8796093022208.0e+3, "8796093022208000"),
    ];

    private static readonly (double Number, string Str)[] _fromStringTests =
    [
        (double.NaN, "    NaN"),
        (double.PositiveInfinity, "Infinity    "),
        (double.NegativeInfinity, "    -Infinity"),
        (1, "1."),
        (1, "1.0   "),
        (1, "+1"),
        (1, "+1."),
        (1, "+1.0"),
        (double.NaN, "whoops"),
        (0, ""),
        (0, "0"),
        (0, "0."),
        (0, "0.0"),
        (0, "0.0000"),
        (0, ".0000"),
        (_negativeZeroValue, "-0"),
        (_negativeZeroValue, "-0."),
        (_negativeZeroValue, "-0.0"),
        (_negativeZeroValue, "-.0"),
        (double.NaN, "."),
        (double.NaN, "e"),
        (double.NaN, ".e"),
        (double.NaN, "+"),
        (0, "0X0"),
        (double.NaN, "e0"),
        (double.NaN, "E0"),
        (double.NaN, "1e"),
        (double.NaN, "1e+"),
        (double.NaN, "1e-"),
        (1, "1e+0"),
        (double.NaN, "++0"),
        (double.NaN, "0_0"),
        (double.PositiveInfinity, "1e1000"),
        (double.NegativeInfinity, "-1e1000"),
        (0, ".0e0"),
        (double.NaN, "0e++0"),
        (10, "0XA"),
        (0b1010, "0b1010"),
        (0b1010, "0B1010"),
        (10, "0o12"), // Go 的 0o12（八进制字面量），C# 无八进制字面量
        (10, "0O12"), // Go 的 0O12
        (0x123456789abcdef0, "0x123456789abcdef0"),
        (0x123456789abcdef0, "0X123456789ABCDEF0"),
        (18446744073709552000d, "0X10000000000000000"),
        (18446744073709597000d, "0X1000000000000A801"),
        (double.NaN, "0B0.0"),
        (1.231235345083403e+91, "12312353450834030486384068034683603046834603806830644850340602384608368034634603680348603864"),
        (double.NaN, "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX8OOOOOOOOOOOOOOOOOOO"),
        (double.PositiveInfinity, "+Infinity"),
        (1234.56, "  \t1234.56  "),
        (double.NaN, "\u200b"),
        (0, " "),
        (0, "\n"),
        (0, "\r"),
        (0, "\r\n"),
        (0, "\u2028"),
        (0, "\u2029"),
        (0, "\t"),
        (0, "\v"),
        (0, "\f"),
        (0, "\uFEFF"),
        (0, "\u00A0"),
        (10000000000000000000, "010000000000000000000"),
        (double.NaN, "0x1.fffffffffffffp1023"), // Make sure Go's extended float syntax doesn't work.
        (double.NaN, "0X_1FFFP-16"),
        (double.NaN, "1_000"), // NumberToString doesn't handle underscores.
        (0, "0x0"),
        (0, "0X0"),
        (double.NaN, "0xOOPS"),
        (0xABCDEF, "0xABCDEF"),
        (0xABCDEF, "0xABCDEF"),
        (0, "0o0"),
        (0, "0O0"),
        (double.NaN, "0o8"),
        (double.NaN, "0O8"),
        (5349, "0o12345"), // Go 的 0o12345
        (5349, "0O12345"), // Go 的 0O12345
        (0, "0b0"),
        (0, "0B0"),
        (double.NaN, "0b2"),
        (double.NaN, "0b2"),
        (0b10101, "0b10101"),
        (0b10101, "0B10101"),
        (double.NaN, "1.f"),
        (double.NaN, "1.e"),
        (double.NaN, "1.0ef"),
        (double.NaN, "1.0e"),
        (double.NaN, ".f"),
        (double.NaN, ".e"),
        (double.NaN, ".0ef"),
        (double.NaN, ".0e"),
        (double.NaN, "a.f"),
        (double.NaN, "a.e"),
        (double.NaN, "a.0ef"),
        (double.NaN, "a.0e"),
    ];

    public static TheoryData<double, string> StringTestsData => ToTheoryData(_stringTests);

    public static TheoryData<double, string> FromStringTestsData => ToTheoryData(_fromStringTests);

    private static TheoryData<double, string> ToTheoryData((double Number, string Str)[] tests)
    {
        var data = new TheoryData<double, string>();
        foreach (var test in tests)
        {
            data.Add(test.Number, test.Str);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StringTestsData))]
    public void TestString(double number, string str)
    {
        output.WriteLine($"{number}");
        Assert.Equal(str, ((Number)number).String());
    }

    [Theory]
    [MemberData(nameof(StringTestsData))]
    public void TestFromStringStringTests(double number, string str)
    {
        output.WriteLine(str);
        JsTest.AssertEqualNumber(FromString(str), (Number)number);
        JsTest.AssertEqualNumber(FromString(str + " "), (Number)number);
        JsTest.AssertEqualNumber(FromString(" " + str), (Number)number);
    }

    [Theory]
    [MemberData(nameof(FromStringTestsData))]
    public void TestFromStringFromStringTests(double number, string str)
    {
        output.WriteLine(str);
        JsTest.AssertEqualNumber(FromString(str), (Number)number);
    }

    [Theory]
    [MemberData(nameof(StringTestsData))]
    public void TestStringRoundtrip(double number, string str)
    {
        output.WriteLine($"{number}");
        Assert.Equal(str, FromString(str).String());
    }

    [Fact]
    public void TestStringJSStringTests()
    {
        JsTest.SkipIfNoNodeJS();

        // These tests should roundtrip both ways.
        var results = JsTest.GetStringResultsFromJS(_stringTests);
        Assert.Equal(_stringTests.Length, results.Length);
        for (var i = 0; i < _stringTests.Length; i++)
        {
            output.WriteLine($"{_stringTests[i].Number}");
            JsTest.AssertEqualNumber(results[i].Number, (Number)_stringTests[i].Number);
            Assert.Equal(_stringTests[i].Str, results[i].Str);
        }
    }

    [Fact]
    public void TestStringJSFromStringTests()
    {
        JsTest.SkipIfNoNodeJS();

        // These tests should convert the string to the same number.
        var results = JsTest.GetStringResultsFromJS(_fromStringTests);
        Assert.Equal(_fromStringTests.Length, results.Length);
        for (var i = 0; i < _fromStringTests.Length; i++)
        {
            output.WriteLine($"fromString \"{_fromStringTests[i].Str}\"");
            JsTest.AssertEqualNumber(results[i].Number, (Number)_fromStringTests[i].Number);
        }
    }

    // Go: FuzzStringJS 的种子语料（`f.Add(float64(test.number))`，stringTests 与 fromStringTests 各一遍）。
    [Fact]
    public void TestFuzzStringJSSeeds()
    {
        JsTest.SkipIfNoNodeJS();

        var seeds = _stringTests.Concat(_fromStringTests)
            .Select(static test => (Number: test.Number, Str: ((Number)test.Number).String()))
            .ToArray();
        var results = JsTest.GetStringResultsFromJS(seeds);
        Assert.Equal(seeds.Length, results.Length);

        for (var i = 0; i < seeds.Length; i++)
        {
            var n = (Number)seeds[i].Number;
            var nStr = seeds[i].Str;
            var nToJSStr = results[i].Str;
            var nStrToJSNumber = results[i].Number;

            Assert.Equal(nStr, nToJSStr);
            JsTest.AssertEqualNumber(n, nStrToJSNumber);
        }
    }

    // Go: FuzzFromStringJS 的种子语料（`f.Add(test.str)`，两个表各一遍）。
    [Fact]
    public void TestFuzzFromStringJSSeeds()
    {
        JsTest.SkipIfNoNodeJS();

        var seeds = _stringTests.Concat(_fromStringTests)
            .Select(static test => (Number: 0d, Str: test.Str))
            .ToArray();
        var results = JsTest.GetStringResultsFromJS(seeds);
        Assert.Equal(seeds.Length, results.Length);

        for (var i = 0; i < seeds.Length; i++)
        {
            var s = seeds[i].Str;
            if (s.Length > 350)
            {
                continue;
            }

            var n = FromString(s);
            JsTest.AssertEqualNumber(n, results[i].Number);
        }
    }
}
