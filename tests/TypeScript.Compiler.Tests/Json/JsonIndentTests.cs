// 验收 3：缩进形状。Go json/v2 的多行形状（docs/json.md §1.2 实测）：
//   `"k": v`（冒号后有空格）、换行 LF、数组/对象元素各占一行、**无**尾部换行；compact 是 `"k":v`。
// 缩进串由调用方给：showConfig 用 "    "、tracing/fourslash 用 "  "、lsp 用 "\t"、init 用 ""。
using Xunit;

using TypeScript.Json;
using JsonDecoder = TypeScript.Json.Decoder;
using JsonEncoder = TypeScript.Json.Encoder;
using static TypeScript.Json.Json;

namespace TypeScript.Tests;

public class JsonIndentTests
{
    private static Dictionary<string, object?> Sample() => new()
    {
        ["k"] = "v",
        ["a"] = new List<object?> { 1, 2 },
    };

    private const string _sampleCompact = "{\"k\":\"v\",\"a\":[1,2]}";
    private const string _sampleIndented4 = "{\n    \"k\": \"v\",\n    \"a\": [\n        1,\n        2\n    ]\n}";

    [Fact]
    public void TestCompactShape()
    {
        Assert.Equal(_sampleCompact, Text(Marshal(Sample())));
    }

    [Fact]
    public void TestMarshalIndentFourSpaces()
    {
        var text = Text(MarshalIndent(Sample(), "", "    "));
        Assert.Equal(_sampleIndented4, text);
        Assert.DoesNotContain('\r', text);
        Assert.False(text.EndsWith('\n'));
    }

    [Fact]
    public void TestMarshalIndentEmptyIndentFallsBackToCompact()
    {
        // json.go 的特判：prefix 和 indent 都空 → 直接 compact（否则 WithIndent 会开多行）。
        Assert.Equal(_sampleCompact, Text(MarshalIndent(Sample(), "", "")));
    }

    [Fact]
    public void TestWithIndentEmptyMeansMultiLineWithoutIndentation()
    {
        // Go 的 jsontext.WithIndent("") 依然是多行（只是缩进串为空），与 MarshalIndent(…, "", "") 不同。
        Assert.Equal(
            "{\n\"k\": \"v\",\n\"a\": [\n1,\n2\n]\n}",
            Text(Marshal(Sample(), WithIndent(""))));
    }

    [Fact]
    public void TestMarshalIndentTwoSpacesAndTab()
    {
        Assert.Equal(
            "{\n  \"k\": \"v\",\n  \"a\": [\n    1,\n    2\n  ]\n}",
            Text(MarshalIndent(Sample(), "", "  ")));
        Assert.Equal(
            "{\n\t\"k\": \"v\",\n\t\"a\": [\n\t\t1,\n\t\t2\n\t]\n}",
            Text(MarshalIndent(Sample(), "", "\t")));
    }

    [Fact]
    public void TestMarshalIndentPrefixAndNonUniformIndent()
    {
        // prefix 和“非空格/制表符的缩进串”走 OutputFilterStream 的逐行重排路径：
        // Go 的行首是 prefix + indent×层级（prefix 在层级 0 的行上也要写）。
        Assert.Equal(
            "{\n\t> \"k\": \"v\",\n\t> \"a\": [\n\t> > 1,\n\t> > 2\n\t> ]\n\t}",
            Text(MarshalIndent(Sample(), "\t", "> ")));
    }

    [Fact]
    public void TestNestedShapeMatchesGoShowConfigShape()
    {
        // 形状与 artifacts/json-oracle/ctrl.out（tsc --showConfig 的真实字节）一致：对象里嵌对象、数组里嵌字符串。
        var value = new Dictionary<string, object?>
        {
            ["compilerOptions"] = new Dictionary<string, object?>
            {
                ["target"] = "es6",
                ["types"] = new List<object?> { "ctrl\u0001x", "del\u007f" },
            },
            ["files"] = new List<object?> { "./a.ts" },
        };

        Assert.Equal(
            "{\n"
            + "    \"compilerOptions\": {\n"
            + "        \"target\": \"es6\",\n"
            + "        \"types\": [\n"
            + "            \"ctrl\\u0001x\",\n"
            + "            \"del\u007f\"\n"
            + "        ]\n"
            + "    },\n"
            + "    \"files\": [\n"
            + "        \"./a.ts\"\n"
            + "    ]\n"
            + "}",
            Text(MarshalIndent(value, "", "    ")));
    }

    [Fact]
    public void TestIndentedNestedArraysAndRawNumbers()
    {
        // 回归：Utf8JsonWriter.WriteRawValue 不参与缩进，数组里的数字要由 Encoder 自己补换行+缩进；
        // 对象成员的数字紧跟 ": "，不能补。
        var value = new Dictionary<string, object?>
        {
            ["n"] = 520.2,
            ["a"] = new List<object?> { new List<object?> { 1, 2 }, -2.109808898695963e16 },
        };

        Assert.Equal(
            "{\n"
            + "    \"n\": 520.2,\n"
            + "    \"a\": [\n"
            + "        [\n"
            + "            1,\n"
            + "            2\n"
            + "        ],\n"
            + "        -21098088986959630\n"
            + "    ]\n"
            + "}",
            Text(MarshalIndent(value, "", "    ")));
    }

    [Fact]
    public void TestMarshalIndentWriteToStream()
    {
        var stream = new MemoryStream();
        MarshalIndentWrite(stream, Sample(), "", "    ");
        Assert.Equal(_sampleIndented4, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void TestIndentOptionsThroughEncoder()
    {
        var stream = new MemoryStream();
        var encoder = new JsonEncoder(stream, WithIndent("  "));
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("k"));
        encoder.WriteToken(JsonToken.String("v"));
        encoder.WriteToken(EndObject);
        encoder.Dispose();

        Assert.Equal("{\n  \"k\": \"v\"\n}", System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string Text(byte[] bytes)
    {
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
