// ported from: internal/packagejson/expected_test.go
//
// 与 Go 测试的差异（逐条）：
// 1) Go 用局部结构体 + reflect 标签解码；C# 无反射（docs §3.6），测试形状自己实现 `IJsonUnmarshaler`。
// 2) Go 的 `Expected[T]` 是值类型：字段缺失/解不出来时 `Value` 是 T 的**零值**（`string` 为 `""`）；C# 是 class，
//    `default(string)` 是 `null`，故 `p.Version.Value` / `p.Main.Value` 断言为 null（见 Expected.cs 文件头第 6 条）。
//    `Null`/`Valid`/`IsPresent` 的语义与 Go 完全一致。
using System.Text;
using TypeScript.Json;
using TypeScript.PackageJson;
using Xunit;
using static TypeScript.Json.Json;

// `Decoder` 与 `System.Text.Decoder`（ImplicitUsings 带进来）同名 → 别名消歧（repo 既有做法：JsonRoundTripTests）。
using Decoder = TypeScript.Json.Decoder;

// `PackageJson`（命名空间名）遮蔽了同名类型 → 别名访问（见 PackageJsonTests.cs 的说明）。
using PackageJsonClass = TypeScript.PackageJson.PackageJson;

namespace TypeScript.Tests;

public class ExpectedTests
{
    // Go: TestExpected 里的匿名 `type packageJson struct { … }`（expected_test.go:14-19）
    private sealed class PackageJsonShape : IJsonUnmarshaler
    {
        public Expected<string> Name = new();

        public Expected<string> Version = new();

        public Expected<object?> Exports = new();

        public Expected<string> Main = new();

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
                    case "name":
                        Name.UnmarshalJSONFrom(dec);
                        break;
                    case "version":
                        Version.UnmarshalJSONFrom(dec);
                        break;
                    case "exports":
                        Exports.UnmarshalJSONFrom(dec);
                        break;
                    case "main":
                        Main.UnmarshalJSONFrom(dec);
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
    public void UnmarshalExpected()
    {
        const string jsonString = """
            {
                "name": "test",
                "version": 2,
                "exports": null
            }
            """;

        var p = UnmarshalNew<PackageJsonShape>(Encoding.UTF8.GetBytes(jsonString));

        Assert.True(p.Name.Valid);
        Assert.Equal("test", p.Name.Value);

        Assert.False(p.Version.Valid);
        Assert.Null(p.Version.Value);

        Assert.True(p.Exports.Null);
        Assert.False(p.Exports.Valid);

        Assert.False(p.Main.Valid);
        Assert.False(p.Main.Null);
        Assert.Null(p.Main.Value);
    }

    // Go 侧没有直接测 ExpectedOf；它是 Parse 的期望值构造器（packagejson_test.go 全靠它），这里钉住
    // "已出现且合法"语义 + `GetValue`/`TryGetValue` 的对应关系。
    [Fact]
    public void ExpectedOfMarksValuePresentAndValid()
    {
        var expected = PackageJsonClass.ExpectedOf("test");
        Assert.True(expected.IsPresent());
        Assert.True(expected.IsValid());
        Assert.Equal("string", expected.ExpectedJSONType());
        Assert.Equal("string", expected.ActualJSONType());
        Assert.True(expected.TryGetValue(out var value));
        Assert.Equal("test", value);

        var missing = new Expected<string>();
        Assert.False(missing.IsPresent());
        Assert.False(missing.IsValid());
        Assert.Equal("string", missing.ExpectedJSONType());
        Assert.Equal("", missing.ActualJSONType());
        Assert.False(missing.TryGetValue(out var missingValue));
        Assert.Null(missingValue);
    }
}
