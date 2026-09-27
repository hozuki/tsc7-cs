// 流式 API：Encoder（对应 Go 的 jsontext.Encoder）与 Decoder（对应 jsontext.Decoder）。
// 这里的形状直接照抄 TSGO 的两处真实用法：
//   * internal/collections/ordered_map.go 的 MarshalJSONTo：WriteToken(BeginObject) + 逐对
//     `json.MarshalEncode(enc, key)` / `json.MarshalEncode(enc, value)`（String token 落在对象里就是属性名）
//   * internal/lsp/lsproto/structcodec.go 的 unmarshalStruct：`PeekKind()` / `ReadValue()`（名字连引号）/ SkipValue
using System.Text;
using Xunit;

using TypeScript.Json;
using static TypeScript.Json.Json;
using JsonDecoder = TypeScript.Json.Decoder;
using JsonEncoder = TypeScript.Json.Encoder;

namespace TypeScript.Tests;

public class JsonStreamingTests
{
    private sealed class TokenMap : IJsonMarshaler, IJsonUnmarshaler
    {
        public List<KeyValuePair<string, string>> Entries { get; } = [];

        public void MarshalJSONTo(JsonEncoder encoder)
        {
            encoder.WriteToken(BeginObject);
            foreach (var entry in Entries)
            {
                MarshalEncode(encoder, entry.Key);
                MarshalEncode(encoder, entry.Value);
            }

            encoder.WriteToken(EndObject);
        }

        public void UnmarshalJSONFrom(JsonDecoder decoder)
        {
            if (decoder.ReadToken().Kind != JsonKind.BeginObject)
            {
                throw new JsonException("expected object");
            }

            while (decoder.PeekKind() != JsonKind.EndObject)
            {
                // Go 的 UnmarshalDecode：名字 token 当字符串解（ordered_map.go 就是这样取 key 的）。
                var key = decoder.Decode<string>() ?? "";
                var value = decoder.Decode<string>() ?? "";
                Entries.Add(new KeyValuePair<string, string>(key, value));
            }

            decoder.ReadToken();
        }
    }

    [Fact]
    public void TestEncoderTokenStreamMirrorsOrderedMap()
    {
        var map = new TokenMap();
        map.Entries.Add(new KeyValuePair<string, string>("b", "2"));
        map.Entries.Add(new KeyValuePair<string, string>("a", "1"));

        var bytes = Marshal(map);
        Assert.Equal("{\"b\":\"2\",\"a\":\"1\"}", Encoding.UTF8.GetString(bytes));

        var back = UnmarshalNew<TokenMap>(bytes);
        Assert.Equal(2, back.Entries.Count);
        Assert.Equal("b", back.Entries[0].Key);
        Assert.Equal("2", back.Entries[0].Value);
        Assert.Equal("a", back.Entries[1].Key);
        Assert.Equal("1", back.Entries[1].Value);
    }

    [Fact]
    public void TestEncoderStackDepthAndMarshalEncode()
    {
        var stream = new MemoryStream();
        var encoder = new JsonEncoder(stream);
        Assert.Equal(0, encoder.StackDepth);
        encoder.WriteToken(BeginArray);
        Assert.Equal(1, encoder.StackDepth);
        MarshalEncode(encoder, 1);
        MarshalEncode(encoder, "two");
        MarshalEncode(encoder, null);
        encoder.WriteToken(EndArray);
        Assert.Equal(0, encoder.StackDepth);
        encoder.Dispose();

        Assert.Equal("[1,\"two\",null]", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void TestEncoderWriteRawValuePassesThroughVerbatim()
    {
        var stream = new MemoryStream();
        var encoder = new JsonEncoder(stream);
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("n"));
        encoder.WriteValue("1e+21");
        encoder.WriteToken(EndObject);
        encoder.Dispose();

        Assert.Equal("{\"n\":1e+21}", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void TestWritingValueWhereNameIsExpectedThrows()
    {
        var stream = new MemoryStream();
        using var encoder = new JsonEncoder(stream);
        encoder.WriteToken(BeginObject);
        var exception = Assert.Throws<JsonException>(() => MarshalEncode(encoder, 1));
        Assert.Contains("property name", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "{\"b\":1,\"a\":2}")]
    [InlineData(true, "{\"a\":2,\"b\":1}")]
    public void TestDeterministicSortsMapKeys(bool deterministic, string expected)
    {
        var value = new Dictionary<string, object?> { ["b"] = 1, ["a"] = 2 };
        var bytes = deterministic ? Marshal(value, Deterministic(true)) : Marshal(value);
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void TestDeterministicThroughMarshalEncodeAndNestedMaps()
    {
        var value = new Dictionary<string, object?>
        {
            ["z"] = new Dictionary<string, object?> { ["d"] = 1, ["c"] = 2 },
            ["a"] = 3,
        };

        var stream = new MemoryStream();
        var encoder = new JsonEncoder(stream);
        MarshalEncode(encoder, value, Deterministic(true));
        encoder.Dispose();

        Assert.Equal(
            "{\"a\":3,\"z\":{\"c\":2,\"d\":1}}",
            Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void TestDecoderTokenApiMirrorsStructCodec()
    {
        // 与 lsproto 的 unmarshalStruct 相同的读法：PeekKind / ReadValue（名字连引号）/ SkipValue。
        var decoder = NewDecoder("{\"a\":1,\"skip\":[1,2],\"b\":\"x\"}"u8.ToArray());
        Assert.Equal(JsonKind.BeginObject, decoder.PeekKind());
        Assert.Equal(JsonKind.BeginObject, decoder.ReadToken().Kind);
        Assert.Equal(1, decoder.StackDepth);

        Assert.Equal(JsonKind.String, decoder.PeekKind());
        Assert.Equal("\"a\"", Encoding.UTF8.GetString(decoder.ReadValue()));
        Assert.Equal(1, decoder.Decode<int>());

        Assert.Equal("\"skip\"", Encoding.UTF8.GetString(decoder.ReadValue()));
        Assert.Equal(JsonKind.BeginArray, decoder.PeekKind());
        decoder.SkipValue();

        Assert.Equal("\"b\"", Encoding.UTF8.GetString(decoder.ReadValue()));
        Assert.Equal("x", decoder.Decode<string>());

        Assert.Equal(JsonKind.EndObject, decoder.PeekKind());
        Assert.Equal(JsonKind.EndObject, decoder.ReadToken().Kind);
        Assert.Equal(0, decoder.StackDepth);
        Assert.Equal(28, decoder.InputOffset);
    }

    [Fact]
    public void TestDecoderDecodeIntoExistingTarget()
    {
        var map = new Dictionary<string, object?> { ["old"] = 1 };
        var decoder = NewDecoder("{\"k\":\"v\"}"u8.ToArray());
        decoder.DecodeInto(map);
        Assert.Single(map);
        Assert.Equal("v", map["k"]);
    }

    [Fact]
    public void TestDecoderNumbersKeepGoText()
    {
        var token = NewDecoder("[1e+21,5e-324]"u8.ToArray()).PeekKind();
        Assert.Equal(JsonKind.BeginArray, token);

        var decoder = NewDecoder("[1e+21,5e-324]"u8.ToArray());
        decoder.ReadToken();
        Assert.Equal(1e21, decoder.Decode<double>());
        Assert.Equal(5e-324, decoder.Decode<double>());
    }

    [Fact]
    public void TestStreamingDecoderBuffersWholeInput()
    {
        // 记录 C# 侧的偏离（docs/json.md §2）：Decoder 构造时把输入整体读进内存。
        var stream = new MemoryStream("{\"a\":1}"u8.ToArray());
        var decoder = NewDecoder(stream);
        Assert.Equal(JsonKind.BeginObject, decoder.PeekKind());
        var map = decoder.DecodeNew<Dictionary<string, object?>>();
        Assert.Equal(1.0, map["a"]);
    }
}
