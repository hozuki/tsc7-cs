// ported from: internal/packagejson/jsonvalue_test.go
//
// 与 Go 测试的差异：
// 1) Go 的测试用局部结构体 + reflect 标签解码；C# 无反射（docs §3.6），测试形状自己实现 `IJsonUnmarshaler`
//    （逐字段分派，语义与 Go 的 reflect 一致：未知字段跳过）。
// 2) `float64(2)` → `2.0`，并用 `Assert.IsType<double>(...)` 钉住 C# 侧"数字解成 double"这一形状
//    （docs/json.md §2：`object?` 形状里的数字是 double）。
using System.Text;
using TypeScript.Json;
using TypeScript.PackageJson;
using Xunit;
using static TypeScript.Json.Json;

// `Decoder` 与 `System.Text.Decoder`（ImplicitUsings 带进来）同名 → 别名消歧（repo 既有做法：JsonRoundTripTests）。
using Decoder = TypeScript.Json.Decoder;

namespace TypeScript.Tests;

public class JsonValueTests
{
    // Go: testJSONValue 里的匿名 `type packageJson struct { … }`（jsonvalue_test.go:19-27）
    private sealed class PackageJsonShape : IJsonUnmarshaler
    {
        public JSONValue Private = new();

        public JSONValue False = new();

        public JSONValue Name = new();

        public JSONValue Version = new();

        public JSONValue Exports = new();

        public JSONValue Imports = new();

        public JSONValue NotPresent = new();

        public void UnmarshalJSONFrom(Decoder dec)
        {
            if (dec.ReadToken().Kind != JsonKind.BeginObject)
            {
                throw new JsonException("expected object");
            }

            while (dec.PeekKind() != JsonKind.EndObject)
            {
                var name = dec.ReadName();
                switch (name)
                {
                    case "private":
                        Private.UnmarshalJSONFrom(dec);
                        break;
                    case "false":
                        False.UnmarshalJSONFrom(dec);
                        break;
                    case "name":
                        Name.UnmarshalJSONFrom(dec);
                        break;
                    case "version":
                        Version.UnmarshalJSONFrom(dec);
                        break;
                    case "exports":
                        Exports.UnmarshalJSONFrom(dec);
                        break;
                    case "imports":
                        Imports.UnmarshalJSONFrom(dec);
                        break;
                    case "notPresent":
                        NotPresent.UnmarshalJSONFrom(dec);
                        break;
                    default:
                        dec.SkipValue();
                        break;
                }
            }

            dec.ReadToken();
        }
    }

    [Fact]
    public void UnmarshalJSONValue()
    {
        const string jsonString = """
            {
                "private": true,
                "false": false,
                "name": "test",
                "version": 2,
                "exports": {
                    ".": {
                        "import": "./test.ts",
                        "default": "./test.ts"
                    },
                    "./test": [
                        "./test1.ts",
                        "./test2.ts",
                        null
                    ],
                    "./null": null
                },
                "imports": null
            }
            """;

        var p = UnmarshalNew<PackageJsonShape>(Encoding.UTF8.GetBytes(jsonString));

        Assert.Equal(JSONValueType.Boolean, p.Private.Type);
        Assert.Equal(true, p.Private.Value);

        Assert.Equal(JSONValueType.String, p.Name.Type);
        Assert.Equal("test", p.Name.Value);

        Assert.Equal(JSONValueType.Number, p.Version.Type);
        Assert.Equal(2.0, Assert.IsType<double>(p.Version.Value));

        Assert.Equal(JSONValueType.Object, p.Exports.Type);
        Assert.Equal(3, p.Exports.AsObject()!.Size());
        Assert.Equal(JSONValueType.Object, p.Exports.AsObject()!.GetOrZero(".")!.Type);
        Assert.Equal("./test.ts", p.Exports.AsObject()!.GetOrZero(".")!.AsObject()!.GetOrZero("import")!.Value);

        Assert.Equal(JSONValueType.Array, p.Exports.AsObject()!.GetOrZero("./test")!.Type);
        Assert.Equal(3, p.Exports.AsObject()!.GetOrZero("./test")!.AsArray().Length);
        Assert.Equal("./test1.ts", p.Exports.AsObject()!.GetOrZero("./test")!.AsArray()[0].Value);
        Assert.Equal("./test2.ts", p.Exports.AsObject()!.GetOrZero("./test")!.AsArray()[1].Value);
        Assert.Equal(JSONValueType.Null, p.Exports.AsObject()!.GetOrZero("./test")!.AsArray()[2].Type);

        Assert.Equal(JSONValueType.Null, p.Exports.AsObject()!.GetOrZero("./null")!.Type);

        Assert.Equal(JSONValueType.Null, p.Imports.Type);
        Assert.Null(p.Imports.Value);

        Assert.Equal(JSONValueType.NotPresent, p.NotPresent.Type);
        Assert.Null(p.NotPresent.Value);
    }

    // Go 的 IsFalsy/IsPresent/AsString 等访问器在 jsonvalue.go 里没有直接测试；这里补最小覆盖，
    // 因为 expected.go 与 module 的消费方依赖它们（每个断言都对应 Go 的语义）。
    [Fact]
    public void AccessorSemantics()
    {
        const string jsonString = """{ "f": false, "t": true, "zero": 0, "empty": "", "text": "x" }""";

        var p = UnmarshalNew<AccessorShape>(Encoding.UTF8.GetBytes(jsonString));

        Assert.True(p.F.IsPresent());
        Assert.True(p.F.IsFalsy());
        Assert.True(p.T.IsPresent());
        Assert.False(p.T.IsFalsy());
        Assert.True(p.Zero.IsFalsy());
        Assert.True(p.Empty.IsFalsy());
        Assert.False(p.Text.IsFalsy());
        Assert.Equal("x", p.Text.AsString());
        Assert.Equal("unknown(0)", JSONValueType.NotPresent.String());
        Assert.Equal("object", JSONValueType.Object.String());
    }

    private sealed class AccessorShape : IJsonUnmarshaler
    {
        public JSONValue F = new();

        public JSONValue T = new();

        public JSONValue Zero = new();

        public JSONValue Empty = new();

        public JSONValue Text = new();

        public void UnmarshalJSONFrom(Decoder dec)
        {
            if (dec.ReadToken().Kind != JsonKind.BeginObject)
            {
                throw new JsonException("expected object");
            }

            while (dec.PeekKind() != JsonKind.EndObject)
            {
                var name = dec.ReadName();
                switch (name)
                {
                    case "f":
                        F.UnmarshalJSONFrom(dec);
                        break;
                    case "t":
                        T.UnmarshalJSONFrom(dec);
                        break;
                    case "zero":
                        Zero.UnmarshalJSONFrom(dec);
                        break;
                    case "empty":
                        Empty.UnmarshalJSONFrom(dec);
                        break;
                    case "text":
                        Text.UnmarshalJSONFrom(dec);
                        break;
                    default:
                        dec.SkipValue();
                        break;
                }
            }

            dec.ReadToken();
        }
    }
}
