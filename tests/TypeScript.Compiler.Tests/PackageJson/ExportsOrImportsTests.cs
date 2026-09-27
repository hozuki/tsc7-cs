// ported from: internal/packagejson/exportsorimports_test.go
//
// 与 Go 测试的差异：测试形状（Go 的匿名结构体）在 C# 里实现 `IJsonUnmarshaler`（无反射，docs §3.6），
// 逐字段分派与 Go 的 reflect 一致；其余断言与 Go 一一对应。
using System.Text;
using TypeScript.Json;
using TypeScript.PackageJson;
using Xunit;
using static TypeScript.Json.Json;

// `Decoder` 与 `System.Text.Decoder`（ImplicitUsings 带进来）同名 → 别名消歧（repo 既有做法：JsonRoundTripTests）。
using Decoder = TypeScript.Json.Decoder;

namespace TypeScript.Tests;

public class ExportsOrImportsTests
{
    // Go: testExports 里的匿名 `type Exports struct { Imports …; Exports … }`（exportsorimports_test.go:18-21）
    private sealed class ExportsShape : IJsonUnmarshaler
    {
        public ExportsOrImports Imports = new();

        public ExportsOrImports Exports = new();

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
                    case "imports":
                        Imports.UnmarshalJSONFrom(dec);
                        break;
                    case "exports":
                        Exports.UnmarshalJSONFrom(dec);
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
    public void UnmarshalExportsOrImports()
    {
        const string jsonString = """
            {
                "imports": {
                    "#foo": {
                        "import": "./foo.ts"
                    }
                },
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
                }
            }
            """;

        var e = UnmarshalNew<ExportsShape>(Encoding.UTF8.GetBytes(jsonString));

        Assert.True(e.Exports.IsSubpaths());
        Assert.Equal(3, e.Exports.AsObject()!.Size());
        Assert.True(e.Exports.AsObject()!.GetOrZero(".")!.IsConditions());
        Assert.True(e.Exports.AsObject()!.GetOrZero(".")!.AsObject()!.GetOrZero("import")!.Type == JSONValueType.String);
        Assert.Equal(JSONValueType.Null, e.Exports.AsObject()!.GetOrZero("./test")!.AsArray()[2].Type);
        Assert.True(e.Exports.AsObject()!.GetOrZero("./null")!.Type == JSONValueType.Null);

        Assert.True(e.Imports.IsImports());
        Assert.Equal(1, e.Imports.AsObject()!.Size());
        Assert.True(e.Imports.AsObject()!.GetOrZero("#foo")!.IsConditions());
        Assert.True(e.Imports.AsObject()!.GetOrZero("#foo")!.AsObject()!.GetOrZero("import")!.Type == JSONValueType.String);
    }

    // Go 的 initObjectKind 还有几个分支在 Go 侧没有直接测试；它们决定 module 解析的行为，
    // 这里按 exportsorimports.go:60-84 的语义钉住。
    [Fact]
    public void ObjectKindClassification()
    {
        // 空键被忽略（len(k) == 0 不参与判定）→ 既无 "." 也无 "#" → Conditions
        Assert.True(DecodeExports("""{ "": {} }""").IsConditions());
        Assert.True(DecodeExports("""{ ".": "./a.ts" }""").IsSubpaths());
        Assert.True(DecodeExports("""{ "#b": "./b.ts" }""").IsImports());

        // "其它键"（非 "."/"#" 开头）与 "." 混用 → Invalid（seenOther && seenDot）
        var mixed = DecodeExports("""{ ".": "./a.ts", "foo": "./b.ts" }""");
        Assert.False(mixed.IsSubpaths());
        Assert.False(mixed.IsImports());
        Assert.False(mixed.IsConditions());

        // "." 与 "#" 混用但都是已知前缀 → 仍按 "." 优先判成 Subpaths（Go 的分支顺序如此）
        Assert.True(DecodeExports("""{ ".": "./a.ts", "#b": "./b.ts" }""").IsSubpaths());
    }

    private static ExportsOrImports DecodeExports(string json)
    {
        var value = new ExportsOrImports();
        value.UnmarshalJSONFrom(new Decoder(Encoding.UTF8.GetBytes(json)));
        return value;
    }
}
