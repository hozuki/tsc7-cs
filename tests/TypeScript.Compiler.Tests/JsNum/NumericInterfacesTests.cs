// C# 侧新增：Number 的 System.Numerics 运算符接口族测试（Go 无对应物）。
//
// 这些测试证明两件事：
// 1) 泛型算法只按**运算符接口**约束写，就能拿 Number 实例化（这也是这些接口存在的意义）；
// 2) 新增的 `%` / `++` / `--` / 隐式数值转换在 JS 语义上与既有方法一致（NaN、±0、Inf 的边界）。
using System.Numerics;
using TypeScript.JsNum;
using Xunit;
using Xunit.Abstractions;
using static TypeScript.JsNum.JsNum;

namespace TypeScript.Tests;

public class NumericInterfacesTests(ITestOutputHelper output)
{
    // ---- 泛型算法：约束里只出现运算符接口，函数体里没有任何 Number 的名字 ----

    private static T Sum<T>(ReadOnlySpan<T> values)
        where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
    {
        var sum = T.AdditiveIdentity;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum;
    }

    private static T Max<T>(ReadOnlySpan<T> values)
        where T : IComparisonOperators<T, T, bool>
    {
        var max = values[0];
        foreach (var value in values)
        {
            if (value > max)
            {
                max = value;
            }
        }

        return max;
    }

    private static T Average<T>(ReadOnlySpan<T> values, T count)
        where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, IDivisionOperators<T, T, T>
    {
        return Sum(values) / count;
    }

    private static T Scale<T>(T value, T factor)
        where T : IMultiplyOperators<T, T, T>
    {
        return value * factor;
    }

    private static bool IsMultiple<T>(T value, T divisor)
        where T : IModulusOperators<T, T, T>, IEqualityOperators<T, T, bool>, IAdditiveIdentity<T, T>
    {
        return value % divisor == T.AdditiveIdentity;
    }

    private static T Bump<T>(T value)
        where T : IIncrementOperators<T>, IDecrementOperators<T>
    {
        value++;   // 后缀：先取值再 +1
        --value;   // 前缀 -1
        value++;   // 净效果 +1
        return value;
    }

    private static T NegateThenAddIdentity<T>(T value, out T identity)
        where T : IUnaryNegationOperators<T, T>, IUnaryPlusOperators<T, T>, IAdditiveIdentity<T, T>, IAdditionOperators<T, T, T>
    {
        identity = T.AdditiveIdentity;
        return -value + +value + identity;
    }

    [Fact]
    public void GenericAlgorithmsConsumeNumber()
    {
        Number[] values = [0.5, 1.5, 2.0];
        double[] doubles = [0.5, 1.5, 2.0];
        output.WriteLine($"Sum<Number>(0.5, 1.5, 2.0) = {Sum(values)}");
        output.WriteLine($"Max<Number>(0.5, 1.5, 2.0) = {Max(values)}");
        output.WriteLine($"Average<Number>(0.5, 1.5, 2.0, 3) = {Average(values, 3)}");
        output.WriteLine($"Scale<Number>(3, 2) = {Scale((Number)3, 2)}");
        output.WriteLine($"IsMultiple<Number>(7, 7) = {IsMultiple((Number)7, 7)}");
        output.WriteLine($"Bump<Number>(2) = {Bump((Number)2)}");
        output.WriteLine($"NegateThenAddIdentity<Number>(123.5) = {NegateThenAddIdentity((Number)123.5, out _)}");
        output.WriteLine($"Sum<double>(0.5, 1.5, 2.0) = {Sum(doubles)}");

        // 同一份泛型代码对 double 与 Number 都成立，且结果逐位一致（Number 只是包了一层 double）。
        Assert.Equal(Sum(doubles), (double)Sum(values));

        Assert.Equal(4.0, (double)Sum(values));
        Assert.Equal(2.0, (double)Max(values));
        Assert.Equal(4.0 / 3.0, (double)Average(values, 3));
        Assert.Equal(6.0, (double)Scale((Number)3, 2));
        Assert.True(IsMultiple((Number)7, 7));
        Assert.False(IsMultiple((Number)7, 2));
        Assert.Equal(3.0, (double)Bump((Number)2));
        Assert.Equal(0.0, (double)NegateThenAddIdentity((Number)123.5, out var identity));
        Assert.Equal(0.0, (double)identity);

        // 泛型 Max 也能消费 double：证明约束确实是"按接口"而不是被 Number 特化。
        Assert.Equal(2.0, Max(doubles));
    }

    // ---- 隐式数值转换 + 运算符：Go 里 `n * 2` 那种写法 ----

    [Fact]
    public void ImplicitNumericConversionsKeepGoStyleOperators()
    {
        Number n = 2;            // int → Number
        Number m = 0.5;          // double → Number
        Number l = 4L;           // long → Number
        Number f = 1.5f;         // float → Number

        Assert.Equal(2.0, (double)n);
        Assert.Equal(0.5, (double)m);
        Assert.Equal(4.0, (double)l);
        Assert.Equal(1.5, (double)f);

        // Go: n * 2、n + 0.5、n < 0、n == 3
        Assert.Equal(4.0, (double)(n * 2));
        Assert.Equal(2.5, (double)(n + 0.5));
        Assert.False(n < 0);
        Assert.True(n == 2);

        // 仍是 IEEE 的浮点运算，不是整数运算。
        Assert.Equal(0.30000000000000004, (double)((Number)0.1 + 0.2));

        // 显式转换两个方向都还在（`(Number)1.5` 只是不再必要）。
        Assert.Equal(1.5, (double)(Number)1.5);
        Assert.Equal(7, (int)(double)(Number)7);
    }

    // ---- `%`（IJS/IEEE 的 remainder）----

    [Theory]
    [InlineData(5.0, 2.0, 1.0)]
    [InlineData(-5.0, 2.0, -1.0)]
    [InlineData(5.5, 2.0, 1.5)]
    [InlineData(-5.5, 2.0, -1.5)]
    [InlineData(2.0, double.PositiveInfinity, 2.0)]
    [InlineData(0.0, 3.0, 0.0)]
    public void ModulusMatchesJsRemainder(double left, double right, double expected)
    {
        Number a = left;
        Number b = right;
        Assert.Equal(expected, (double)(a % b));
        Assert.Equal((double)a.Remainder(b), (double)(a % b));
    }

    [Fact]
    public void ModulusNaNandZeroBoundaries()
    {
        Number one = 1;
        Assert.True(((Number)double.NaN % one).IsNaN());
        Assert.True((one % double.NaN).IsNaN());
        Assert.True((one % 0).IsNaN());
        Assert.True(((Number)double.PositiveInfinity % one).IsNaN());

        // JS: -0 % 3 是 -0（符号保留）。
        var negativeZeroRemainder = (Number)double.NegativeZero % 3;
        Assert.True(double.IsNegative((double)negativeZeroRemainder));
    }

    // ---- `++` / `--` ----

    [Fact]
    public void IncrementAndDecrementMatchJs()
    {
        Number n = 3;
        Assert.Equal(3.0, (double)n++);
        Assert.Equal(4.0, (double)n);
        Assert.Equal(5.0, (double)++n);
        Assert.Equal(5.0, (double)n);

        Assert.Equal(5.0, (double)n--);
        Assert.Equal(4.0, (double)n);
        Assert.Equal(3.0, (double)--n);

        // float 上没有溢出概念，checked 与 unchecked 同义（.NET 的 checked ++ 默认实现就是走 ++）：
        // MaxValue + 1 按 IEEE 舍入回 MaxValue，Infinity + 1 仍是 Infinity，都不会抛。
        Number big = double.MaxValue;
        checked
        {
            big++;
        }

        Assert.Equal(double.MaxValue, (double)big);

        Number infinity = double.PositiveInfinity;
        checked
        {
            infinity++;
        }

        Assert.True(infinity.IsInf());
    }

    // ---- 比较运算符：NaN 全 false，±0 相等 ----

    [Fact]
    public void ComparisonOperatorsFollowIeeeNaNandNegativeZero()
    {
        // 两个各自算出来的 NaN（不能写 `nan == nan`：编译器会报 CS1718 同变量比较）。
        Number nan = NaN();
        Number otherNan = NaN();
        Assert.False(nan == otherNan);
        Assert.True(nan != otherNan);
        Assert.False(nan < otherNan);
        Assert.False(nan <= otherNan);
        Assert.False(nan > otherNan);
        Assert.False(nan >= otherNan);
        Assert.False(nan < 0);
        Assert.False(nan <= 0);
        Assert.False(nan > 0);
        Assert.False(nan >= 0);

        Assert.True((Number)double.NegativeZero == 0);
        Assert.True((Number)double.NegativeZero >= 0);
    }

    // ---- 除零 / 单位元 ----

    [Fact]
    public void DivisionByZeroAndIdentities()
    {
        Number one = 1;
        Assert.True(double.IsPositiveInfinity((double)(one / 0)));
        Assert.True(double.IsNegativeInfinity((double)(one / double.NegativeZero)));
        Assert.True((one / 0).IsInf());
        Assert.True(((Number)0 / 0).IsNaN());

        Assert.Equal(0.0, (double)Number.AdditiveIdentity);
        Assert.Equal(1.0, (double)Number.MultiplicativeIdentity);

        // 单位元在泛型算法里就是"加法起点"，这里也用运算符验证一遍。
        Assert.Equal(5.0, (double)(Number.AdditiveIdentity + 5));
        Assert.Equal(5.0, (double)(5 * Number.MultiplicativeIdentity));
    }

    // ---- 既有 JS 语义没有被这些接口改动 ----

    [Fact]
    public void JsSemanticsUnchanged()
    {
        Assert.Equal("NaN", ((Number)double.NaN).ToString());
        Assert.Equal("Infinity", ((Number)double.PositiveInfinity).ToString());
        Assert.Equal("-Infinity", ((Number)double.NegativeInfinity).ToString());
        Assert.Equal("0.1", ((Number)0.1).ToString());
        Assert.Equal("123", ((Number)123).ToString());
        Assert.Equal("0", ((Number)0).ToString());

        Assert.Equal(16.0, (double)FromString("0x10"));
        Assert.Equal(0.0, (double)FromString(""));
        Assert.Equal(12.0, (double)FromString(" 12 "));
        Assert.True(FromString("NaN").IsNaN());
        Assert.True(double.IsPositiveInfinity((double)FromString("Infinity")));
    }
}
