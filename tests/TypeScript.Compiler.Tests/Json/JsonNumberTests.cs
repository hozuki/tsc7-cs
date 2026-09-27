// 验收 2：数字。float64 必须按 Go 的规则格式化后 WriteRawValue：
//   abs != 0 && (abs < 1e-6 || abs >= 1e21) → 指数形式（小写 e、带符号、指数不留前导零），否则十进制形式。
// ryu 向量取自 TSGO 自己的 internal/jsnum/ryu_test.go（它的期望值就是 json.Marshal(float64) 的输出，
// 且被 TSGO 的 Number.String() 钉住）。
using System.Globalization;
using System.Numerics;
using Xunit;

using static TypeScript.Json.Json;
using TypeScript.Json;

namespace TypeScript.Tests;

public class JsonNumberTests
{
    private static string Text(object? value)
    {
        return System.Text.Encoding.UTF8.GetString(Marshal(value));
    }

    [Theory]
    [InlineData(-2.109808898695963e16, "-21098088986959630")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(2.2250738585072014e-308, "2.2250738585072014e-308")]
    [InlineData(520.2, "520.2")]
    [InlineData(0.0, "0")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(-0.0, "-0")]
    [InlineData(1e-6, "0.000001")]
    [InlineData(1e-5, "0.00001")]
    [InlineData(1.5e16, "15000000000000000")]
    [InlineData(double.MaxValue, "1.7976931348623157e+308")]
    [InlineData(1.0 / 3.0, "0.3333333333333333")]
    [InlineData(100.0, "100")]
    [InlineData(-1.5, "-1.5")]
    public void TestDoubleFormatting(double value, string expected)
    {
        Assert.Equal(expected, Text(value));
    }

    public static TheoryData<double, string> RyuVectors => new()
    {
        { 2.2250738585072014e-308, "2.2250738585072014e-308" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x7fefffffffffffffUL)), "1.7976931348623157e+308" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x0000000000000001UL)), "5e-324" },
        { 2.98023223876953125e-8, "2.9802322387695312e-8" },
        { -2.109808898695963e16, "-21098088986959630" },
        { 4.940656e-318, "4.940656e-318" },
        { 1.18575755e-316, "1.18575755e-316" },
        { 2.989102097996e-312, "2.989102097996e-312" },
        { 9.0608011534336e15, "9060801153433600" },
        { 4.708356024711512e18, "4708356024711512000" },
        { 9.409340012568248e18, "9409340012568248000" },
        { 1.2345678, "1.2345678" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x4830f0cf064dd592UL)), "5.764607523034235e+39" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x4840f0cf064dd592UL)), "1.152921504606847e+40" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x4850f0cf064dd592UL)), "2.305843009213694e+40" },
        { 1.2, "1.2" },
        { 1.23, "1.23" },
        { 1.234, "1.234" },
        { 1.2345, "1.2345" },
        { 1.23456, "1.23456" },
        { 1.234567, "1.234567" },
        { 1.2345678, "1.2345678" },
        { 1.23456789, "1.23456789" },
        { 1.234567895, "1.234567895" },
        { 1.2345678901, "1.2345678901" },
        { 1.23456789012, "1.23456789012" },
        { 1.234567890123, "1.234567890123" },
        { 1.2345678901234, "1.2345678901234" },
        { 1.23456789012345, "1.23456789012345" },
        { 1.234567890123456, "1.234567890123456" },
        { 1.2345678901234567, "1.2345678901234567" },
        { 4.294967294, "4.294967294" },
        { 4.294967295, "4.294967295" },
        { 4.294967296, "4.294967296" },
        { 4.294967297, "4.294967297" },
        { 4.294967298, "4.294967298" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x0040000000000000UL)), "1.7800590868057611e-307" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x007fffffffffffffUL)), "2.8480945388892175e-306" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x0290000000000000UL)), "2.446494580089078e-296" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x029fffffffffffffUL)), "4.8929891601781557e-296" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x4350000000000000UL)), "18014398509481984" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x435fffffffffffffUL)), "36028797018963964" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x1330000000000000UL)), "2.900835519859558e-216" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x133fffffffffffffUL)), "5.801671039719115e-216" },
        { BitConverter.Int64BitsToDouble(unchecked((long)0x3a6fa7161a4d6e0cUL)), "3.196104012172126e-27" },
        { 9007199254740991.0, "9007199254740991" },
        { 9007199254740992.0, "9007199254740992" },
        { 1.0e+0, "1" },
        { 1.2e+1, "12" },
        { 1.23e+2, "123" },
        { 1.234e+3, "1234" },
        { 1.2345e+4, "12345" },
        { 1.23456e+5, "123456" },
        { 1.234567e+6, "1234567" },
        { 1.2345678e+7, "12345678" },
        { 1.23456789e+8, "123456789" },
        { 1.23456789e+9, "1234567890" },
        { 1.234567895e+9, "1234567895" },
        { 1.2345678901e+10, "12345678901" },
        { 1.23456789012e+11, "123456789012" },
        { 1.234567890123e+12, "1234567890123" },
        { 1.2345678901234e+13, "12345678901234" },
        { 1.23456789012345e+14, "123456789012345" },
        { 1.234567890123456e+15, "1234567890123456" },
        { 1.0e+0, "1" },
        { 1.0e+1, "10" },
        { 1.0e+2, "100" },
        { 1.0e+3, "1000" },
        { 1.0e+4, "10000" },
        { 1.0e+5, "100000" },
        { 1.0e+6, "1000000" },
        { 1.0e+7, "10000000" },
        { 1.0e+8, "100000000" },
        { 1.0e+9, "1000000000" },
        { 1.0e+10, "10000000000" },
        { 1.0e+11, "100000000000" },
        { 1.0e+12, "1000000000000" },
        { 1.0e+13, "10000000000000" },
        { 1.0e+14, "100000000000000" },
        { 1.0e+15, "1000000000000000" },
        { 1000000000000001.0, "1000000000000001" },
        { 1000000000000010.0, "1000000000000010" },
        { 1000000000000100.0, "1000000000000100" },
        { 1000000000001000.0, "1000000000001000" },
        { 1000000000010000.0, "1000000000010000" },
        { 1000000000100000.0, "1000000000100000" },
        { 1000000001000000.0, "1000000001000000" },
        { 1000000010000000.0, "1000000010000000" },
        { 1000000100000000.0, "1000000100000000" },
        { 1000001000000000.0, "1000001000000000" },
        { 1000010000000000.0, "1000010000000000" },
        { 1000100000000000.0, "1000100000000000" },
        { 1001000000000000.0, "1001000000000000" },
        { 1010000000000000.0, "1010000000000000" },
        { 1100000000000000.0, "1100000000000000" },
        { 8.0, "8" },
        { 64.0, "64" },
        { 512.0, "512" },
        { 8192.0, "8192" },
        { 65536.0, "65536" },
        { 524288.0, "524288" },
        { 8388608.0, "8388608" },
        { 67108864.0, "67108864" },
        { 536870912.0, "536870912" },
        { 8589934592.0, "8589934592" },
        { 68719476736.0, "68719476736" },
        { 549755813888.0, "549755813888" },
        { 8796093022208.0, "8796093022208" },
        { 70368744177664.0, "70368744177664" },
        { 562949953421312.0, "562949953421312" },
        { 9007199254740992.0, "9007199254740992" },
        { 8.0e+3, "8000" },
        { 64.0e+3, "64000" },
        { 512.0e+3, "512000" },
        { 8192.0e+3, "8192000" },
        { 65536.0e+3, "65536000" },
        { 524288.0e+3, "524288000" },
        { 8388608.0e+3, "8388608000" },
        { 67108864.0e+3, "67108864000" },
        { 536870912.0e+3, "536870912000" },
        { 8589934592.0e+3, "8589934592000" },
        { 68719476736.0e+3, "68719476736000" },
        { 549755813888.0e+3, "549755813888000" },
        { 8796093022208.0e+3, "8796093022208000" },
    };

    [Theory]
    [MemberData(nameof(RyuVectors))]
    public void TestDoubleFormattingMatchesGoRyuVectors(double value, string expected)
    {
        Assert.Equal(expected, Text(value));
    }

    [Fact]
    public void TestIntegerAndIntegerLikeTypes()
    {
        Assert.Equal("0", Text(0));
        Assert.Equal("-2147483648", Text(int.MinValue));
        Assert.Equal("4294967295", Text(uint.MaxValue));
        Assert.Equal("-9223372036854775808", Text(long.MinValue));
        Assert.Equal("18446744073709551615", Text(ulong.MaxValue));
        Assert.Equal("170141183460469231731687303715884105727", Text(Int128.MaxValue));
        Assert.Equal("340282366920938463463374607431768211455", Text(UInt128.MaxValue));
        Assert.Equal("123456789012345678901234567890", Text(BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture)));
        Assert.Equal("1.5", Text(1.5m));
        Assert.Equal("3.4028235e+38", Text(float.MaxValue));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void TestNonFiniteValuesThrow(double value)
    {
        // Go 的 json/v2 对 NaN/±Inf 返回错误（不是 null 也不是 Infinity 字面量）。
        var exception = Assert.Throws<JsonException>(() => Marshal(value));
        Assert.Contains("unsupported value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestNonFiniteFloatThrows()
    {
        Assert.Throws<JsonException>(() => Marshal(float.NaN));
    }

    [Fact]
    public void TestNestedNumbersUseGoFormatting()
    {
        var value = new Dictionary<string, object?>
        {
            ["ts"] = 520.2,
            ["big"] = 1e21,
            ["small"] = 1e-7,
        };
        Assert.Equal(
            "{\"ts\":520.2,\"big\":1e+21,\"small\":1e-7}",
            System.Text.Encoding.UTF8.GetString(Marshal(value)));
    }

    [Fact]
    public void TestNumberRoundTrip()
    {
        var bytes = Marshal(1e-7);
        Assert.Equal(1e-7, Unmarshal<double>(bytes));
        Assert.Equal(520.2, Unmarshal<double>(Marshal(520.2)));
    }
}
