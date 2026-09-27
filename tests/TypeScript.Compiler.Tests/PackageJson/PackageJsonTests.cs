// ported from: internal/packagejson/packagejson_test.go
//
// 与 Go 测试的差异（逐条）：
//
// 1) **BenchmarkPackageJSON 不搬**：Go benchmark 依赖 `repo.RootPath()`/`parser`/`testutil/filefixture` 与
//    Go 的 testing.B，本机也没有 Go 工具链；它测的是"用 reflect 解 JSON 的速度"，C# 侧解码路径完全不同，
//    没有可比的基准（报告里已写明）。
//
// 2) Go 用 `assert.DeepEqual(got, want, cmpopts.IgnoreUnexported(…))` 比较整个 `Fields`（忽略未导出字段）；
//    C# 的测试程序集开了 AOT 分析器（不能用反射做通用深比较），这里改成逐字段的等价断言
//    （`AssertFieldsEqual`），比较的是 `Null`/`Valid`/`Value`/`IsPresent()`/`ActualJSONType()` 这五个可观察面。
//
// 3) `Expected[T]` 在 C# 是 class、`default(T)` 对引用类型是 null：Go 里"解不出来"的字段 `Value` 是 T 的零值
//    （`Expected[string]` 为 `""`），C# 是 `null` —— 断言按 C# 语义写（见 Expected.cs 文件头第 6 条）。
//
// 4) 表驱动用 xunit 的 `[Theory]`/`MemberData`（docs/architecture.md §5），三个用例（duplicate names /
//    content mapper / invalid typescript field is ignored）与 Go 的表一一对应。
using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Json;
using TypeScript.PackageJson;
using Xunit;
using static TypeScript.Json.Json;

// `PackageJson` 既是命名空间名又是类型名：从别的命名空间里 `PackageJson.Parse(...)` 会先解析到**命名空间**
// （architecture.md §2 的同名陷阱 → CS0234），所以这里按 §2 的既有做法用别名访问包级函数/类型
// （也可 `using static TypeScript.PackageJson.PackageJson;` 后用裸名；本文件不能用裸名是因为测试方法本身叫 `Parse`）。
using PackageJsonClass = TypeScript.PackageJson.PackageJson;

namespace TypeScript.Tests;

public class PackageJsonTests
{
    public static TheoryData<string, string> ParseCases() => new()
    {
        {
            "duplicate names",
            """
            {
                "name": "test-package",
                "name": "test-package",
                "version": "1.0.0"
            }
            """
        },
        {
            "content mapper",
            """
            {
                "name": "test-package",
                "typescript": {
                    "contentMapper": { "exec": ["mapper"], "dynamicConfig": true }
                }
            }
            """
        },
        {
            "invalid typescript field is ignored",
            """{ "name": "test-package", "typescript": "invalid" }"""
        },
    };

    [Theory]
    [MemberData(nameof(ParseCases))]
    public void Parse(string name, string content)
    {
        var got = PackageJsonClass.Parse(Encoding.UTF8.GetBytes(content));
        var want = name switch
        {
            "duplicate names" => new Fields
            {
                HeaderFields =
                {
                    Name = PackageJsonClass.ExpectedOf("test-package"),
                    Version = PackageJsonClass.ExpectedOf("1.0.0"),
                },
            },
            "content mapper" => new Fields
            {
                HeaderFields = { Name = PackageJsonClass.ExpectedOf("test-package") },
                ContentMapper = PackageJsonClass.ExpectedOf(new ContentMapperFields
                {
                    Exec = PackageJsonClass.ExpectedOf(new[] { "mapper" }),
                    DynamicConfig = PackageJsonClass.ExpectedOf(true),
                }),
            },
            "invalid typescript field is ignored" => new Fields
            {
                HeaderFields = { Name = PackageJsonClass.ExpectedOf("test-package") },
            },
            _ => throw new ArgumentException($"unknown case: {name}"),
        };

        AssertFieldsEqual(got, want);
    }

    // Go: assert.DeepEqual(t, got, want, cmpopts.IgnoreUnexported(…))。`IgnoreUnexported` 涵盖的正是
    // Go 的 `actualJSONType`/`objectKind` 这些未导出字段（C# 侧是 `internal` 的 `_actualJSONType`/`_objectKind`），
    // 所以这里也**不比较** ActualJSONType —— 它在"解码出来的字段"（"object"/"string"…）与
    // `ExpectedOf(...)` 造出来的期望值（ExpectedJSONType 的名字，如 ContentMapperFields 是 "unknown"）
    // 之间本来就可能不同（Go 的测试正因如此才忽略它）。语义等价的断言是 Null/Valid/IsPresent/Value。
    private static void AssertFieldsEqual(Fields got, Fields want)
    {
        AssertTypeValidatedEqual(got.HeaderFields.Name, want.HeaderFields.Name);
        AssertTypeValidatedEqual(got.HeaderFields.Version, want.HeaderFields.Version);
        AssertTypeValidatedEqual(got.HeaderFields.Type, want.HeaderFields.Type);

        AssertTypeValidatedEqual(got.PathFields.TSConfig, want.PathFields.TSConfig);
        AssertTypeValidatedEqual(got.PathFields.Main, want.PathFields.Main);
        AssertTypeValidatedEqual(got.PathFields.Types, want.PathFields.Types);
        AssertTypeValidatedEqual(got.PathFields.Typings, want.PathFields.Typings);
        AssertJsonValueEqual(got.PathFields.TypesVersions, want.PathFields.TypesVersions);
        AssertJsonValueEqual(got.PathFields.Imports, want.PathFields.Imports);
        AssertJsonValueEqual(got.PathFields.Exports, want.PathFields.Exports);

        AssertTypeValidatedEqual(got.DependencyFields.Dependencies, want.DependencyFields.Dependencies);
        AssertTypeValidatedEqual(got.DependencyFields.DevDependencies, want.DependencyFields.DevDependencies);
        AssertTypeValidatedEqual(got.DependencyFields.PeerDependencies, want.DependencyFields.PeerDependencies);
        AssertTypeValidatedEqual(got.DependencyFields.OptionalDependencies, want.DependencyFields.OptionalDependencies);

        AssertContentMapperEqual(got.ContentMapper, want.ContentMapper);
    }

    private static void AssertTypeValidatedEqual<T>(Expected<T> got, Expected<T> want)
    {
        Assert.Equal(want.Null, got.Null);
        Assert.Equal(want.Valid, got.Valid);
        Assert.Equal(want.IsPresent(), got.IsPresent());
        AssertValuesEqual(want.Value, got.Value);
    }

    private static void AssertContentMapperEqual(Expected<ContentMapperFields> got, Expected<ContentMapperFields> want)
    {
        Assert.Equal(want.Null, got.Null);
        Assert.Equal(want.Valid, got.Valid);
        Assert.Equal(want.IsPresent(), got.IsPresent());

        if (want.Valid && got.Valid)
        {
            AssertTypeValidatedEqual(got.Value.Exec, want.Value.Exec);
            AssertTypeValidatedEqual(got.Value.CompilerOptions, want.Value.CompilerOptions);
            AssertTypeValidatedEqual(got.Value.DynamicConfig, want.Value.DynamicConfig);
        }
        else
        {
            Assert.Null(got.Value);
            Assert.Null(want.Value);
        }
    }

    private static void AssertJsonValueEqual(JSONValue got, JSONValue want)
    {
        Assert.Equal(want.Type, got.Type);
        AssertValuesEqual(want.Value, got.Value);
    }

    private static void AssertValuesEqual(object? want, object? got)
    {
        switch (want)
        {
            case null:
                Assert.Null(got);
                return;
            case string text:
                Assert.Equal(text, got);
                return;
            case bool flag:
                Assert.Equal(flag, got);
                return;
            case string[] strings:
                Assert.Equal(strings, Assert.IsType<string[]>(got));
                return;
            case Dictionary<string, string> map:
                var gotMap = Assert.IsType<Dictionary<string, string>>(got);
                Assert.Equal(map.Count, gotMap.Count);
                foreach (var pair in map)
                {
                    Assert.Equal(pair.Value, gotMap[pair.Key]);
                }

                return;
            default:
                Assert.Equal(want, got);
                return;
        }
    }

    // Go 的 Parse 在 JSON 语法错误时返回 error；C# 的对应形状是 TryParse（见 PackageJson.cs 文件头第 4b 条）。
    [Fact]
    public void TryParseReturnsZeroFieldsOnInvalidJson()
    {
        Assert.False(PackageJsonClass.TryParse(Encoding.UTF8.GetBytes("{ \"name\": "), out var fields));

        // Go: `return Fields{}, err` —— 失败时字段全零（IsPresent/Valid 都是 false、actualJSONType 为空串）。
        Assert.False(fields.HeaderFields.Name.Valid);
        Assert.False(fields.HeaderFields.Name.IsPresent());
        Assert.Equal("", fields.HeaderFields.Name.ActualJSONType());
        Assert.Null(fields.HeaderFields.Name.Value);
    }

    [Fact]
    public void ParseThrowsOnInvalidJson()
    {
        Assert.Throws<JsonException>(() => PackageJsonClass.Parse(Encoding.UTF8.GetBytes("{ \"name\": ")));
    }

    // 依赖 TSGO 检出的 fixture（Go 的 packageJsonFixtures 只被 BenchmarkPackageJSON 用到）：在真实的大文件
    // package.json（date-fns 有 exports 子路径、依赖表；repo 自己的 package.json 有 devDependencies/workspaces）上
    // 走一遍 ReadValue/解码路径，并把结果与**独立的正则抽取**（同一个文件的顶层 name/version）对齐 ——
    // 这样断言的是"解出来的字段确实是那一项"，而不是硬编码 TSGO 仓库当前的内容。
    // 文件不存在（没有 TSGO 检出）时跳过，写法与 ParserCorpusTests/DiagnosticsTests 的 TSGO_ROOT 探测一致。
    [Theory]
    [InlineData("package.json")]
    [InlineData("date-fns.json")]
    public void ParseFixture(string fileName)
    {
        var root = Environment.GetEnvironmentVariable("TSGO_ROOT") is { Length: > 0 } value
            ? value
            : @"D:\Source\GitHub\TypeScript-Go";
        var path = fileName == "package.json"
            ? System.IO.Path.Combine(root, "package.json")
            : System.IO.Path.Combine(root, "tsc", "testdata", "fixtures", "packagejson", fileName);
        if (!File.Exists(path))
        {
            return; // 需要 TSGO 检出（TSGO_ROOT），没有就跳过。
        }

        var text = File.ReadAllText(path);
        var fields = PackageJsonClass.Parse(File.ReadAllBytes(path));

        Assert.True(fields.HeaderFields.Name.Valid);
        Assert.Equal(ExtractField(text, "name"), fields.HeaderFields.Name.Value);
        Assert.True(fields.HeaderFields.Version.Valid);
        Assert.Equal(ExtractField(text, "version"), fields.HeaderFields.Version.Value);

        if (fileName == "date-fns.json")
        {
            // date-fns 的 exports 全是 "." 开头的子路径映射 → JSONValue/子路径分类在真实数据上的检查。
            Assert.True(fields.PathFields.Exports.IsSubpaths());
            Assert.Equal(JSONValueType.String, fields.PathFields.Exports.AsObject()!.GetOrZero("./package.json")!.Type);
        }
        else
        {
            // repo 自己的 package.json 有 devDependencies（Expected<Dictionary<string,string>>）。
            Assert.True(fields.DependencyFields.DevDependencies.Valid);
            Assert.True(fields.DependencyFields.DevDependencies.Value.Count > 0);
        }
    }

    // 独立的抽样器：取顶层（第一次出现）的 `"field": "value"`。
    private static string ExtractField(string text, string field)
    {
        var match = Regex.Match(text, "\"" + field + "\"\\s*:\\s*\"([^\"]*)\"");
        Assert.True(match.Success, $"fixture 里找不到字段 {field}");
        return match.Groups[1].Value;
    }
}
