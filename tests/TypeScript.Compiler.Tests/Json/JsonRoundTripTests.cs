// 验收 4：往返 + 重复属性名。AOT 下没有反射映射（docs/json.md §2），所以：
//   * `object?` 形状（Dictionary<string, object?> / List<object?>）用 Unmarshal<object?>(…)
//   * 结构体/类必须自己实现 IJsonMarshaler/IJsonUnmarshaler，用 UnmarshalNew<T>() 创建
//   * 需要调用方给出实例（数组等）用 UnmarshalInto
using System.Globalization;
using System.Text;
using Xunit;

using TypeScript.Json;
using static TypeScript.Json.Json;
using JsonDecoder = TypeScript.Json.Decoder;
using JsonEncoder = TypeScript.Json.Encoder;

namespace TypeScript.Tests;

public class JsonRoundTripTests
{
    // 自己写的嵌套测试类型：同时实现 IJsonMarshaler 与 IJsonUnmarshaler（对应 Go 的 MarshalerTo/UnmarshalerFrom）。
    private sealed class Point : IJsonMarshaler, IJsonUnmarshaler
    {
        public string Name { get; set; } = "";

        public int X { get; set; }

        public int Y { get; set; }

        public Point? Next { get; set; }

        public void MarshalJSONTo(JsonEncoder encoder)
        {
            encoder.WriteToken(BeginObject);
            encoder.WriteToken(JsonToken.String("name"));
            encoder.WriteToken(JsonToken.String(Name));
            encoder.WriteToken(JsonToken.String("x"));
            encoder.WriteToken(JsonToken.Number(X.ToString(CultureInfo.InvariantCulture)));
            encoder.WriteToken(JsonToken.String("y"));
            encoder.WriteToken(JsonToken.Number(Y.ToString(CultureInfo.InvariantCulture)));
            encoder.WriteToken(JsonToken.String("next"));
            if (Next is null)
            {
                encoder.WriteToken(Null);
            }
            else
            {
                Next.MarshalJSONTo(encoder);
            }

            encoder.WriteToken(EndObject);
        }

        public void UnmarshalJSONFrom(JsonDecoder decoder)
        {
            var start = decoder.ReadToken();
            if (start.Kind != JsonKind.BeginObject)
            {
                throw new JsonException("expected object");
            }

            while (decoder.PeekKind() != JsonKind.EndObject)
            {
                var name = decoder.ReadToken().StringValue;
                switch (name)
                {
                    case "name":
                        Name = decoder.Decode<string>() ?? "";
                        break;
                    case "x":
                        X = decoder.Decode<int>();
                        break;
                    case "y":
                        Y = decoder.Decode<int>();
                        break;
                    case "next":
                        if (decoder.PeekKind() == JsonKind.Null)
                        {
                            decoder.ReadToken();
                            Next = null;
                        }
                        else
                        {
                            Next = decoder.DecodeNew<Point>();
                        }

                        break;
                    default:
                        decoder.SkipValue();
                        break;
                }
            }

            decoder.ReadToken(); // EndObject
        }
    }

    // 只实现写侧的异常类型（用来断言“必须实现 IJsonMarshaler”的报错）。
    private sealed class Plain
    {
        public int X { get; set; } = 1;
    }

    private sealed class ByteBag : IJsonMarshaler, IJsonUnmarshaler
    {
        public byte[] Data { get; set; } = [];

        public void MarshalJSONTo(JsonEncoder encoder)
        {
            encoder.WriteToken(BeginObject);
            encoder.WriteToken(JsonToken.String("data"));
            encoder.WriteToken(JsonToken.String(Convert.ToBase64String(Data)));
            encoder.WriteToken(EndObject);
        }

        public void UnmarshalJSONFrom(JsonDecoder decoder)
        {
            decoder.ReadToken(); // {
            while (decoder.PeekKind() != JsonKind.EndObject)
            {
                var name = decoder.ReadToken().StringValue;
                if (name == "data")
                {
                    Data = decoder.Decode<byte[]>() ?? [];
                }
                else
                {
                    decoder.SkipValue();
                }
            }

            decoder.ReadToken(); // }
        }
    }

    [Fact]
    public void TestObjectGraphRoundTrip()
    {
        var value = new Dictionary<string, object?>
        {
            ["s"] = "text\u001a",
            ["n"] = 42.5,
            ["i"] = 7,
            ["b"] = true,
            ["nil"] = null,
            ["arr"] = new List<object?> { 1, "two", new Dictionary<string, object?> { ["deep"] = "yes" } },
            ["bytes"] = new byte[] { 1, 2, 3 },
            ["enum"] = DayOfWeek.Friday,
        };

        var bytes = Marshal(value);
        Assert.Equal(
            "{\"s\":\"text\\u001a\",\"n\":42.5,\"i\":7,\"b\":true,\"nil\":null,"
            + "\"arr\":[1,\"two\",{\"deep\":\"yes\"}],\"bytes\":\"AQID\",\"enum\":5}",
            Encoding.UTF8.GetString(bytes));

        var map = Assert.IsType<Dictionary<string, object?>>(Unmarshal<object?>(bytes));
        Assert.Equal("text\u001a", map["s"]);
        Assert.Equal(42.5, map["n"]);
        Assert.Equal(7.0, map["i"]); // Go 的 any 把数字解成 float64
        Assert.Equal(true, map["b"]);
        Assert.Null(map["nil"]);
        Assert.Equal("text\u001a", map["s"]);
        Assert.Equal("yes", Assert.IsType<Dictionary<string, object?>>(Assert.IsType<List<object?>>(map["arr"])[2])["deep"]);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), map["bytes"]);
        Assert.Equal(5.0, map["enum"]); // 枚举按底层整数

        // 再序列化逐字节一致（形状 round trip 的强断言）。
        Assert.Equal(bytes, Marshal(map));
    }

    [Fact]
    public void TestTypedDictionaryRoundTrip()
    {
        var value = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var bytes = Marshal(value);
        Assert.Equal("{\"a\":1,\"b\":2}", Encoding.UTF8.GetString(bytes));

        var back = UnmarshalNew<Dictionary<string, int>>(bytes);
        Assert.Equal(1, back["a"]);
        Assert.Equal(2, back["b"]);
    }

    [Fact]
    public void TestIntegerKeysAndTypedLists()
    {
        var value = new Dictionary<int, string> { [1] = "one", [2] = "two" };
        var bytes = Marshal(value, Deterministic(true));
        Assert.Equal("{\"1\":\"one\",\"2\":\"two\"}", Encoding.UTF8.GetString(bytes));

        var back = UnmarshalNew<Dictionary<int, string>>(bytes);
        Assert.Equal("one", back[1]);
        Assert.Equal("two", back[2]);

        var list = UnmarshalNew<List<int>>(Marshal(new List<int> { 3, 4, 5 }));
        Assert.Equal(new List<int> { 3, 4, 5 }, list);
    }

    [Fact]
    public void TestArrayTargetIsFilledInPlace()
    {
        var values = new int[3];
        var bytes = Marshal(new[] { 7, 8, 9 });
        Assert.Equal("[7,8,9]", Encoding.UTF8.GetString(bytes));
        UnmarshalInto<int[]>(bytes, ref values);
        Assert.Equal(new[] { 7, 8, 9 }, values);

        // 长度不一致时报错（C# 数组长度固定，Go 的切片可以重新分配）。
        var tooShort = new int[2];
        Assert.Throws<JsonException>(() => UnmarshalInto<int[]>(bytes, ref tooShort));
    }

    [Fact]
    public void TestCustomTypeRoundTrip()
    {
        var value = new Point
        {
            Name = "root",
            X = 1,
            Y = -2,
            Next = new Point { Name = "child", X = 3, Y = 4 },
        };

        var bytes = Marshal(value);
        Assert.Equal(
            "{\"name\":\"root\",\"x\":1,\"y\":-2,\"next\":{\"name\":\"child\",\"x\":3,\"y\":4,\"next\":null}}",
            Encoding.UTF8.GetString(bytes));

        var back = UnmarshalNew<Point>(bytes);
        Assert.Equal("root", back.Name);
        Assert.Equal(1, back.X);
        Assert.Equal(-2, back.Y);
        Assert.NotNull(back.Next);
        Assert.Equal("child", back.Next!.Name);
        Assert.Equal(3, back.Next.X);
        Assert.Null(back.Next.Next);

        Assert.Equal(bytes, Marshal(back));
    }

    [Fact]
    public void TestCustomTypeRoundTripThroughStream()
    {
        var stream = new MemoryStream();
        MarshalWrite(stream, new Point { Name = "s", X = 5, Y = 6 });
        stream.Position = 0;
        var back = UnmarshalReadNew<Point>(stream);
        Assert.Equal("s", back.Name);
        Assert.Equal(5, back.X);
    }

    [Fact]
    public void TestByteArrayGoesThroughBase64()
    {
        var bytes = Marshal(new ByteBag { Data = [1, 2, 3, 255] });
        Assert.Equal("{\"data\":\"AQID/w==\"}", Encoding.UTF8.GetString(bytes));
        var back = UnmarshalNew<ByteBag>(bytes);
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, back.Data);
    }

    [Fact]
    public void TestDuplicateMemberNamesAreRejectedByDefault()
    {
        var json = "{\"a\":1,\"a\":2}"u8.ToArray();
        var exception = Assert.Throws<JsonException>(() => UnmarshalNew<Dictionary<string, object?>>(json));
        Assert.Contains("duplicate object member name a", exception.Message, StringComparison.Ordinal);

        // 形状解码同样默认拒绝。
        Assert.Throws<JsonException>(() => Unmarshal<object?>(json));

        // AllowDuplicateNames(true) 时容忍，后者胜出（Go json/v2 的行为）。
        var map = UnmarshalNew<Dictionary<string, object?>>(json, AllowDuplicateNames(true));
        Assert.Equal(2.0, map["a"]);

        var shape = Assert.IsType<Dictionary<string, object?>>(
            Unmarshal<object?>(json, AllowDuplicateNames(true)));
        Assert.Equal(2.0, shape["a"]);
    }

    [Fact]
    public void TestRoundTripPreservesNestedShapeWithDuplicateNamesAllowed()
    {
        var json = "{\"o\":{\"k\":1,\"k\":2}}"u8.ToArray();
        var shape = Assert.IsType<Dictionary<string, object?>>(
            Unmarshal<object?>(json, AllowDuplicateNames(true)));
        Assert.Equal(2.0, Assert.IsType<Dictionary<string, object?>>(shape["o"])["k"]);
    }

    [Fact]
    public void TestTypeWithoutInterfacesReportsItsName()
    {
        var write = Assert.Throws<JsonException>(() => Marshal(new Plain()));
        Assert.Contains("must implement IJsonMarshaler", write.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Plain), write.Message, StringComparison.Ordinal);

        var read = Assert.Throws<JsonException>(() => Unmarshal<Plain>("{\"x\":1}"u8.ToArray()));
        Assert.Contains(nameof(Plain), read.Message, StringComparison.Ordinal);
        Assert.Contains("IJsonUnmarshaler", read.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestScalarRoundTrip()
    {
        Assert.Equal("string", Unmarshal<string>("\"string\""u8.ToArray()));
        Assert.True(Unmarshal<bool>("true"u8.ToArray()));
        Assert.Equal(13, Unmarshal<int>(Marshal(13)));
        Assert.Equal(42L, Unmarshal<long>(Marshal(42L)));
        Assert.Equal(1.25, Unmarshal<double>(Marshal(1.25)));
        Assert.Equal('x', Unmarshal<char>("\"x\""u8.ToArray()));
        Assert.Equal(DayOfWeek.Friday, Unmarshal<DayOfWeek>(Marshal(DayOfWeek.Friday)));
        Assert.Null(Unmarshal<string?>("null"u8.ToArray()));
        Assert.Equal(3, Unmarshal<int?>(Marshal(3)));
        Assert.Null(Unmarshal<int?>("null"u8.ToArray()));
    }

    [Fact]
    public void TestMismatchedShapesThrow()
    {
        Assert.Throws<JsonException>(() => Unmarshal<int>("\"text\""u8.ToArray()));
        Assert.Throws<JsonException>(() => Unmarshal<string>("1"u8.ToArray()));
        Assert.Throws<JsonException>(() => Unmarshal<double>("\"1.5\""u8.ToArray()));
    }
}
