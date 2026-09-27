// 验收 1：转义。期望字节来自 tsc --showConfig / --generateTrace 的实测（docs/json.md §1.2）；
// Go json/v2 的转义表：`"`→`\"`、`\`→`\\`、08/0C/0A/0D/09 → 短转义、其余 <0x20 → `\u00xx`（小写十六进制）、
// 0x7F 与 ≥0x80 原样输出 UTF-8 字节、`<`/`>`/`&` 不转义。
using Xunit;

using TypeScript.Json;
using JsonDecoder = TypeScript.Json.Decoder;
using JsonEncoder = TypeScript.Json.Encoder;
using static TypeScript.Json.Json;

namespace TypeScript.Tests;

public class JsonEscapeTests
{
    [Theory]
    [InlineData("控制字符 U+001A 用小写 \\u00xx", "\u001a", "225C753030316122")]
    [InlineData("08/0C/0A/0D/09 用短转义", "\u0008\u000c\n\r\t", "225C625C665C6E5C725C7422")]
    [InlineData("尖括号和 & 不转义", "<>&", "223C3E2622")]
    [InlineData("非 ASCII 原样写 UTF-8（含 astral）", "\u00e9\u4e2d\U0001F600", "22C3A9E4B8ADF09F988022")]
    [InlineData("U+2028/U+2029 原样写 UTF-8", "a\u2028b\u2029c", "2261E280A862E280A96322")]
    [InlineData("引号和反斜杠", "q\"b\\s", "22715C22625C5C7322")]
    [InlineData("U+0000", "\u0000", "225C753030303022")]
    [InlineData("U+000B", "\u000b", "225C753030306222")]
    [InlineData("U+001F", "\u001f", "225C753030316622")]
    [InlineData("tsc ctrl.out 里的 `ctrl\\u0001x`", "\u0001x", "225C75303030317822")]
    [InlineData("tsc ctrl.out 里的 DEL 原样", "del\u007f", "2264656C7F22")]
    [InlineData("字面 U+FFFD 原样（不被二次转义）", "\ufffd", "22EFBFBD22")]
    [InlineData("U+00A0 非断空格原样（UnsafeRelaxed 会转义它）", "\u00a0", "22C2A022")]
    [InlineData("U+0085 原样", "\u0085", "22C28522")]
    [InlineData("U+3000 表意空格原样", "\u3000", "22E3808022")]
    [InlineData("U+FEFF 原样", "\ufeff", "22EFBBBF22")]
    [InlineData("U+FFFF 非字符原样", "\uffff", "22EFBFBF22")]
    [InlineData("私用区 U+E000 原样", "\ue000", "22EE808022")]
    [InlineData("空字符串", "", "2222")]
    public void TestStringEscaping(string name, string input, string expectedHex)
    {
        _ = name;
        Assert.Equal(expectedHex, Convert.ToHexString(Marshal(input)));
    }

    [Fact]
    public void TestEscapingDiffersFromUnsafeRelaxedJsonEscaping()
    {
        // 记录这条实现约束（docs/json.md §1.3、§3）：直接用 STJ 的 UnsafeRelaxedJsonEscaping 会得到
        // \u001A（大写）、\uD83D\uDE00（代理对）、\u2028、\u007F，与 Go 不一致；
        // OutputFilterStream 负责把这些改写成 Go 的字节。摘掉它这个测试就会红。
        var relaxed = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(
            relaxed,
            new System.Text.Json.JsonWriterOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
        {
            writer.WriteStringValue("\u001a\U0001F600\u2028\u007f");
        }

        Assert.Equal(
            "225C75303031415C75443833445C75444530305C75323032385C753030374622",
            Convert.ToHexString(relaxed.ToArray()));

        // Go 的字节：控制字符小写十六进制、astral 原样 4 字节、U+2028 原样 3 字节、0x7F 原样。
        Assert.Equal(
            "225C7530303161F09F9880E280A87F22",
            Convert.ToHexString(Marshal("\u001a\U0001F600\u2028\u007f")));
    }

    [Fact]
    public void TestLoneSurrogateIsReplacedWithUPlusFFFD()
    {
        // 已知缺口（docs/json.md §3 的 unverified 行）：Go 的 AllowInvalidUTF8(true) 会把孤立代理项
        // 按 WTF-8 原样写出（ED A0 BD），而 .NET 的 UTF-16→UTF-8 转换在编码器看到它之前就替换成了 U+FFFD，
        // 所以这里只能得到 U+FFFD 的原始字节。没有 baseline 钉住这个行为；将来若能开 AllowUnsafeBlocks
        // 自写 JavaScriptEncoder，这个测试应当改成 ED A0 BD。
        Assert.Equal("2261EFBFBD6222", Convert.ToHexString(Marshal("a\uD83Db")));
    }

    [Fact]
    public void TestEscapingInsideKeysAndArrays()
    {
        var value = new Dictionary<string, object?>
        {
            ["a\u001ab"] = new List<object?> { "\u2028", 1 },
        };
        Assert.Equal(
            "7B22615C753030316162223A5B22E280A8222C315D7D",
            Convert.ToHexString(Marshal(value)));
    }
}
