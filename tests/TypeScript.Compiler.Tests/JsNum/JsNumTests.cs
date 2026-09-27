// ported from: internal/jsnum/jsnum_test.go
//
// 移植映射：
//   * Go 的 `[]struct{...}` 表驱动 + t.Run(name, ...) → xunit 的 [Theory] + [MemberData]；用例名通过
//     ITestOutputHelper 打进测试输出（见 StringUtil/UtilTests.cs 的说明）。
//   * Go 的 Node.js 交叉验证子测试（t.Run("Node", ...)）→ 单独的 [Fact]（JsTest.EvalBinaryOp/EvalUnaryOp）。
//   * Go 的 BenchmarkToInt32 未移植（xunit 没有 benchmark 形状）。
//   * Go 的 `assertEqualNumber` / `assertWithinOneULP` / `numberFromBits` / `numberToBits` 等 test helper
//     → JsTest.cs。
using Xunit;
using Xunit.Abstractions;
using TypeScript.JsNum;
using static TypeScript.JsNum.JsNum;

namespace TypeScript.Tests;

public class JsNumTests(ITestOutputHelper output)
{
    private static readonly double negativeZeroValue = (double)negativeZero;

    public static TheoryData<string, double, int> ToInt32Tests => new()
    {
        { "0.0", 0, 0 },
        { "-0.0", negativeZeroValue, 0 },
        { "NaN", double.NaN, 0 },
        { "+Inf", double.PositiveInfinity, 0 },
        { "-Inf", double.NegativeInfinity, 0 },
        { "MaxInt32", int.MaxValue, int.MaxValue },
        { "MaxInt32+1", (long)int.MaxValue + 1, int.MinValue },
        { "MinInt32", int.MinValue, int.MinValue },
        { "MinInt32-1", (long)int.MinValue - 1, int.MaxValue },
        { "MIN_SAFE_INTEGER", (double)MinSafeInteger, 1 },
        { "MIN_SAFE_INTEGER-1", (double)MinSafeInteger - 1, 0 },
        { "MIN_SAFE_INTEGER+1", (double)MinSafeInteger + 1, 2 },
        { "MAX_SAFE_INTEGER", (double)MaxSafeInteger, -1 },
        { "MAX_SAFE_INTEGER-1", (double)MaxSafeInteger - 1, -2 },
        { "MAX_SAFE_INTEGER+1", (double)MaxSafeInteger + 1, 0 },
        { "-8589934590", -8589934590, 2 },
        { "0xDEADBEEF", 0xDEADBEEF, -559038737 },
        { "4294967808", 4294967808, 512 },
        { "-0.4", -0.4, 0 },
        { "SmallestNonzeroFloat64", double.Epsilon, 0 },
        { "-SmallestNonzeroFloat64", -double.Epsilon, 0 },
        { "MaxFloat64", double.MaxValue, 0 },
        { "-MaxFloat64", -double.MaxValue, 0 },
        { "Largest subnormal number", BitConverter.Int64BitsToDouble(0x000FFFFFFFFFFFFF), 0 },
        { "Smallest positive normal number", BitConverter.Int64BitsToDouble(0x0010000000000000), 0 },
        { "Largest normal number", double.MaxValue, 0 },
        { "-Largest normal number", -double.MaxValue, 0 },
        { "1.0", 1.0, 1 },
        { "-1.0", -1.0, -1 },
        { "1e308", 1e308, 0 },
        { "-1e308", -1e308, 0 },
        { "math.Pi", Math.PI, 3 },
        { "-math.Pi", -Math.PI, -3 },
        { "math.E", Math.E, 2 },
        { "-math.E", -Math.E, -2 },
        { "0.5", 0.5, 0 },
        { "-0.5", -0.5, 0 },
        { "0.49999999999999994", 0.49999999999999994, 0 },
        { "-0.49999999999999994", -0.49999999999999994, 0 },
        { "0.5000000000000001", 0.5000000000000001, 0 },
        { "-0.5000000000000001", -0.5000000000000001, 0 },
        { "2^31 + 0.5", 2147483648.5, -2147483648 },
        { "-2^31 - 0.5", -2147483648.5, -2147483648 },
        { "2^40", 1099511627776, 0 },
        { "-2^40", -1099511627776, 0 },
        { "TypeFlagsNarrowable", 536624127, 536624127 },
    };

    [Theory]
    [MemberData(nameof(ToInt32Tests))]
    public void TestToInt32(string name, double input, int want)
    {
        output.WriteLine($"{name} ({input})");
        Assert.Equal(want, ((Number)input).ToInt32());
    }

    [Fact]
    public void TestToInt32Node()
    {
        JsTest.SkipIfNoNodeJS();

        var inputs = ToInt32Tests.Select(static row => (Number)(double)row[1]).ToArray();
        var zeros = new Number[inputs.Length];
        var jsResults = JsTest.EvalBinaryOp("a | b", inputs, zeros);
        for (var i = 0; i < inputs.Length; i++)
        {
            JsTest.AssertEqualNumber((Number)inputs[i].ToInt32(), jsResults[i]);
        }
    }

    public static TheoryData<double, double> BitwiseNOTTests => new()
    {
        // Original pairs: ~(-2147483649) == ~(2147483647)
        { -2147483649, -2147483648 },
        { 2147483647, -2147483648 },
        // Original pairs: ~(-4294967296) == ~(0)
        { -4294967296, -1 },
        { 0, -1 },
        // Original pairs: ~(2147483648) == ~(-2147483648)
        { 2147483648, 2147483647 },
        { -2147483648, 2147483647 },
        // Original pairs: ~(4294967296) == ~(0)
        { 4294967296, -1 },
    };

    [Theory]
    [MemberData(nameof(BitwiseNOTTests))]
    public void TestBitwiseNOT(double x, double want)
    {
        output.WriteLine($"~{x}");
        JsTest.AssertEqualNumber(((Number)x).BitwiseNOT(), (Number)want);
    }

    [Fact]
    public void TestBitwiseNOTNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = BitwiseNOTTests.Select(static row => (Number)(double)row[0]).ToArray();
        var jsResults = JsTest.EvalUnaryOp("~a", xs);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertEqualNumber(xs[i].BitwiseNOT(), jsResults[i]);
        }
    }

    public static TheoryData<double, double, double> BitwiseANDTests => new()
    {
        { 0, 0, 0 },
        { 0, 1, 0 },
        { 1, 0, 0 },
        { 1, 1, 1 },
    };

    [Theory]
    [MemberData(nameof(BitwiseANDTests))]
    public void TestBitwiseAND(double x, double y, double want)
    {
        output.WriteLine($"{x} & {y}");
        JsTest.AssertEqualNumber(((Number)x).BitwiseAND((Number)y), (Number)want);
    }

    [Fact]
    public void TestBitwiseANDNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = BitwiseANDTests.Select(static row => (Number)(double)row[0]).ToArray();
        var ys = BitwiseANDTests.Select(static row => (Number)(double)row[1]).ToArray();
        var jsResults = JsTest.EvalBinaryOp("a & b", xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertEqualNumber(xs[i].BitwiseAND(ys[i]), jsResults[i]);
        }
    }

    public static TheoryData<double, double, double> BitwiseORTests => new()
    {
        { 0, 0, 0 },
        { 0, 1, 1 },
        { 1, 0, 1 },
        { 1, 1, 1 },
    };

    [Theory]
    [MemberData(nameof(BitwiseORTests))]
    public void TestBitwiseOR(double x, double y, double want)
    {
        output.WriteLine($"{x} | {y}");
        JsTest.AssertEqualNumber(((Number)x).BitwiseOR((Number)y), (Number)want);
    }

    [Fact]
    public void TestBitwiseORNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = BitwiseORTests.Select(static row => (Number)(double)row[0]).ToArray();
        var ys = BitwiseORTests.Select(static row => (Number)(double)row[1]).ToArray();
        var jsResults = JsTest.EvalBinaryOp("a | b", xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertEqualNumber(xs[i].BitwiseOR(ys[i]), jsResults[i]);
        }
    }

    public static TheoryData<double, double, double> BitwiseXORTests => new()
    {
        { 0, 0, 0 },
        { 0, 1, 1 },
        { 1, 0, 1 },
        { 1, 1, 0 },
    };

    [Theory]
    [MemberData(nameof(BitwiseXORTests))]
    public void TestBitwiseXOR(double x, double y, double want)
    {
        output.WriteLine($"{x} ^ {y}");
        JsTest.AssertEqualNumber(((Number)x).BitwiseXOR((Number)y), (Number)want);
    }

    [Fact]
    public void TestBitwiseXORNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = BitwiseXORTests.Select(static row => (Number)(double)row[0]).ToArray();
        var ys = BitwiseXORTests.Select(static row => (Number)(double)row[1]).ToArray();
        var jsResults = JsTest.EvalBinaryOp("a ^ b", xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertEqualNumber(xs[i].BitwiseXOR(ys[i]), jsResults[i]);
        }
    }

    public static TheoryData<double, double, double> SignedRightShiftTests => new()
    {
        { 1, 0, 1 },
        { 1, 1, 0 },
        { 1, 2, 0 },
        { 1, 31, 0 },
        { 1, 32, 1 },
        { -4, 0, -4 },
        { -4, 1, -2 },
        { -4, 2, -1 },
        { -4, 3, -1 },
        { -4, 4, -1 },
        { -4, 31, -1 },
        { -4, 32, -4 },
        { -4, 33, -2 },
    };

    [Theory]
    [MemberData(nameof(SignedRightShiftTests))]
    public void TestSignedRightShift(double x, double y, double want)
    {
        output.WriteLine($"{x} >> {y}");
        JsTest.AssertEqualNumber(((Number)x).SignedRightShift((Number)y), (Number)want);
    }

    [Fact]
    public void TestSignedRightShiftNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = SignedRightShiftTests.Select(static row => (Number)(double)row[0]).ToArray();
        var ys = SignedRightShiftTests.Select(static row => (Number)(double)row[1]).ToArray();
        var jsResults = JsTest.EvalBinaryOp("a >> b", xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertEqualNumber(xs[i].SignedRightShift(ys[i]), jsResults[i]);
        }
    }

    public static TheoryData<double, double, double> UnsignedRightShiftTests => new()
    {
        { 1, 0, 1 },
        { 1, 1, 0 },
        { 1, 2, 0 },
        { 1, 31, 0 },
        { 1, 32, 1 },
        { -4, 0, 4294967292 },
        { -4, 1, 2147483646 },
        { -4, 2, 1073741823 },
        { -4, 3, 536870911 },
        { -4, 4, 268435455 },
        { -4, 31, 1 },
        { -4, 32, 4294967292 },
        { -4, 33, 2147483646 },
    };

    [Theory]
    [MemberData(nameof(UnsignedRightShiftTests))]
    public void TestUnsignedRightShift(double x, double y, double want)
    {
        output.WriteLine($"{x} >>> {y}");
        JsTest.AssertEqualNumber(((Number)x).UnsignedRightShift((Number)y), (Number)want);
    }

    [Fact]
    public void TestUnsignedRightShiftNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = UnsignedRightShiftTests.Select(static row => (Number)(double)row[0]).ToArray();
        var ys = UnsignedRightShiftTests.Select(static row => (Number)(double)row[1]).ToArray();
        var jsResults = JsTest.EvalBinaryOp("a >>> b", xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertEqualNumber(xs[i].UnsignedRightShift(ys[i]), jsResults[i]);
        }
    }

    public static TheoryData<double, double, double> LeftShiftTests => new()
    {
        { 1, 0, 1 },
        { 1, 1, 2 },
        { 1, 2, 4 },
        { 1, 31, -2147483648 },
        { 1, 32, 1 },
        { -4, 0, -4 },
        { -4, 1, -8 },
        { -4, 2, -16 },
        { -4, 3, -32 },
        { -4, 31, 0 },
        { -4, 32, -4 },
    };

    [Theory]
    [MemberData(nameof(LeftShiftTests))]
    public void TestLeftShift(double x, double y, double want)
    {
        output.WriteLine($"{x} << {y}");
        JsTest.AssertEqualNumber(((Number)x).LeftShift((Number)y), (Number)want);
    }

    [Fact]
    public void TestLeftShiftNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = LeftShiftTests.Select(static row => (Number)(double)row[0]).ToArray();
        var ys = LeftShiftTests.Select(static row => (Number)(double)row[1]).ToArray();
        var jsResults = JsTest.EvalBinaryOp("a << b", xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertEqualNumber(xs[i].LeftShift(ys[i]), jsResults[i]);
        }
    }

    public static TheoryData<double, double, double> RemainderTests => new()
    {
        { double.NaN, 1, double.NaN },
        { 1, double.NaN, double.NaN },
        { double.PositiveInfinity, 1, double.NaN },
        { double.NegativeInfinity, 1, double.NaN },
        { 123, double.PositiveInfinity, 123 },
        { 123, double.NegativeInfinity, 123 },
        { 123, 0, double.NaN },
        { 123, negativeZeroValue, double.NaN },
        { 0, 123, 0 },
        { negativeZeroValue, 123, negativeZeroValue },
        // Normal cases
        { 10, 3, 1 },
        { -10, 3, -1 },
        { 10, -3, 1 },
        { -10, -3, -1 },
        { 5.5, 2, 1.5 },
        { -5.5, 2, -1.5 },
        { 1, 0.5, 0 },
        { -1, 0.5, negativeZeroValue },
        { 1.5, 1, 0.5 },
        { -1.5, 1, -0.5 },
        // Edge cases that prove the bug in the manual formula:
        // The manual formula n - d*(n/d).trunc() accumulates floating-point
        // rounding errors that IEEE 754 fmod (math.Mod) avoids.
        { 7, 0.1, 7 % 0.1 },
        { 7, 0.2, 7 % 0.2 },
        { 7, 0.3, 7 % 0.3 },
        { 100, 0.3, 100 % 0.3 },
    };

    [Theory]
    [MemberData(nameof(RemainderTests))]
    public void TestRemainder(double x, double y, double want)
    {
        output.WriteLine($"{x} % {y}");
        JsTest.AssertEqualNumber(((Number)x).Remainder((Number)y), (Number)want);
    }

    [Fact]
    public void TestRemainderNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = RemainderTests.Select(static row => (Number)(double)row[0]).ToArray();
        var ys = RemainderTests.Select(static row => (Number)(double)row[1]).ToArray();
        var jsResults = JsTest.EvalBinaryOp("a % b", xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertEqualNumber(xs[i].Remainder(ys[i]), jsResults[i]);
        }
    }

    public static TheoryData<double, double, double> ExponentiateTests => new()
    {
        { 2, 3, 8 },
        { double.PositiveInfinity, 3, double.PositiveInfinity },
        { double.PositiveInfinity, -5, 0 },
        { double.NegativeInfinity, 3, double.NegativeInfinity },
        { double.NegativeInfinity, 4, double.PositiveInfinity },
        { double.NegativeInfinity, -3, negativeZeroValue },
        { double.NegativeInfinity, -4, 0 },
        { 0, 3, 0 },
        { 0, -10, double.PositiveInfinity },
        { negativeZeroValue, 3, negativeZeroValue },
        { negativeZeroValue, 4, 0 },
        { negativeZeroValue, -3, double.NegativeInfinity },
        { negativeZeroValue, -4, double.PositiveInfinity },
        { 3, double.PositiveInfinity, double.PositiveInfinity },
        { -3, double.PositiveInfinity, double.PositiveInfinity },
        { 3, double.NegativeInfinity, 0 },
        { -3, double.NegativeInfinity, 0 },
        { double.NaN, 3, double.NaN },
        { 1, double.PositiveInfinity, double.NaN },
        { 1, double.NegativeInfinity, double.NaN },
        { -1, double.PositiveInfinity, double.NaN },
        { -1, double.NegativeInfinity, double.NaN },
        { 1, double.NaN, double.NaN },
        // Cases where math.Pow diverges from V8 by >1 ULP.
        // Expected values are the correctly-rounded IEEE 754 results
        // computed via exact integer arithmetic (big.Int).
        // Cross-engine testing (V8, SpiderMonkey, QuickJS, XS via jsvu)
        // confirmed these match the majority of JS engines.
        { 10, 308, BitConverter.Int64BitsToDouble(unchecked((long)0x7fe1ccf385ebc8a0UL)) },
        { 5, 210, BitConverter.Int64BitsToDouble(unchecked((long)0x5e68557f31326bbbUL)) },
        { 10, 200, BitConverter.Int64BitsToDouble(unchecked((long)0x6974e718d7d7625aUL)) },
    };

    [Theory]
    [MemberData(nameof(ExponentiateTests))]
    public void TestExponentiate(double x, double y, double want)
    {
        output.WriteLine($"{x} ** {y}");
        JsTest.AssertEqualNumber(((Number)x).Exponentiate((Number)y), (Number)want);
    }

    // The ES spec says exponentiate is "implementation-approximated".
    // Different JS engines (V8, SpiderMonkey, JSC) use different pow
    // implementations that can differ by 1 ULP. Allow that tolerance.
    [Fact]
    public void TestExponentiateNode()
    {
        JsTest.SkipIfNoNodeJS();

        var xs = ExponentiateTests.Select(static row => (Number)(double)row[0]).ToArray();
        var ys = ExponentiateTests.Select(static row => (Number)(double)row[1]).ToArray();
        var jsResults = JsTest.EvalBinaryOp("a ** b", xs, ys);
        for (var i = 0; i < xs.Length; i++)
        {
            JsTest.AssertWithinOneULP(xs[i].Exponentiate(ys[i]), jsResults[i]);
        }
    }
}
