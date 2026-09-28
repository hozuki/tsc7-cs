// ported from: internal/api/proto_test.go:1-124
//
// 与 Go 测试的差异（逐条）：
//
// 1) **包名**：Go 的本文件是 `package api_test`（外部测试包），所以引用的是导出面（`api.DocumentIdentifier`…）；
//    C# 的测试程序集靠 `InternalsVisibleTo` 同时看得到内部面，本文件里全部名字加不加 `Api.` 限定都一样。
// 2) `json.Unmarshal([]byte(...), &d)` → `Json.UnmarshalNew<DocumentIdentifier>(bytes)`（`DocumentIdentifier`
//    手写实现 `IJsonUnmarshaler`，见 Api/Proto.cs 文件头第 1 条）；Go 的 `(T, error)` 错误路径 → 抛 `JsonException`。
// 3) `assert.ErrorContains(t, err, msg)` → `var e = Assert.Throws<JsonException>(…); Assert.Contains(msg, e.Message)`。
//    Go 的 `DocumentIdentifier` 错误文本是 `"DocumentIdentifier: expected string or object, got number"`，
//    C# 逐字照搬（见 `Api.JsonKindString`），子串断言与之对齐。
// 4) Go 的表驱动子测试（`t.Run(tt.name, …)`）→ `[Theory]` + `[InlineData]`：xunit 的 InlineData 无法表达
//    "期望抛错"这一类表项，所以第 5 组（`input: "42"`、`err: "expected string or object, got number"`）
//    单独落成一个 `[Fact]`；表项名（plain string / uri object / uri object with unknown fields / empty object）
//    作为 InlineData 上方的注释保留（InlineData 本身只承载数据）。
// 5) `project.ConfiguredProjectID(tspath.Path("/tsconfig.json")).AsID()`：Go 的 `tspath.Path(x)` 是**纯类型转换**
//    （不归一化）→ C# 的 `new Path("/tsconfig.json")`（record struct 构造）；`project.NewSyntheticProjectID(1).AsID()`
//    在 C# 里是 `SyntheticProjectID.NewSyntheticProjectID(1).AsID()`（同名静态工厂，契约 §2）。
// 6) `ast.SourceFileParseOptions{FileName: "/unicode.ts"}`（Path 留零值）→ C# 必须显式写 `Path = new Path("")`：
//    Go 的零值 `tspath.Path` 是 `""`，而 C# 的 `default(Path).Value` 是 `null`（record struct 里的引用字段），
//    显式 `new Path("")` 才是等价输入。
// 7) `strings.Index(text, "=")` 返回**字节**下标（💩 占 4 字节），而 `resp.Pos` 是 UTF-16 → Go 里两者靠
//    `PositionMap.UTF8ToUTF16` 对齐。C# 的内部坐标一律 UTF-16（契约 §1.4），所以 `text.IndexOf("=", Ordinal)`
//    （UTF-16 下标）直接就是 9/10，与 Go 断言里的 `resp.Pos`/`resp.End` 相等；Go 那句
//    `assert.Equal(t, resp.Pos, file.GetPositionMap().UTF8ToUTF16(pos))` 在 C# 里是恒等的
//    （`PositionMap.UTF8ToUTF16` 在 C# 是 `=> utf8Offset`，见 Ast/PositionMap.cs），照原样保留以钉住映射。
// 8) `assert.DeepEqual` 对 `*DiagnosticPositionResponse`/`[]*DiagnosticSourceLineResponse`：C# 的这些类是
//    普通引用类型（没有值相等）⇒ 逐字段断言 / `Assert.Collection`；`assert.Assert(t, pos > 0)` → `Assert.True(pos > 0)`。
// 9) `t.Parallel()` 不移植（xunit 默认按 collection 并行）。
namespace TypeScript.Tests;

using System.Text;
using TypeScript.Api;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Json;
using Xunit;
using static TypeScript.Api.Api;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Json.Json;
using static TypeScript.Parser.Parser;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>internal/api/proto_test.go</c> 的 4 个用例（`TestDocumentIdentifierUnmarshalJSON` 的表被拆成
/// 1 个 `[Theory]` + 1 个 `[Fact]`，见文件头第 4 条 ⇒ C# 侧 5 个测试方法 / 8 个测试用例）。</summary>
public class ProtoTests
{
    // Go 的表项名：plain string / uri object / uri object with unknown fields / empty object
    //（第 5 项 "invalid type" 见 TestDocumentIdentifierUnmarshalJSONInvalidType）。
    /// <summary>Go: <c>TestDocumentIdentifierUnmarshalJSON</c>（proto_test.go:18-67）。</summary>
    [Theory]
    [InlineData("\"foo.ts\"", "foo.ts", "")]
    [InlineData("{\"uri\":\"file:///foo.ts\"}", "", "file:///foo.ts")]
    [InlineData("{\"uri\":\"file:///foo.ts\",\"extra\":true}", "", "file:///foo.ts")]
    [InlineData("{}", "", "")]
    public void TestDocumentIdentifierUnmarshalJSON(string input, string fileName, string uri)
    {
        var d = UnmarshalNew<DocumentIdentifier>(Encoding.UTF8.GetBytes(input));
        Assert.Equal(fileName, d.FileName);
        Assert.Equal(uri, d.URI.Value);
    }

    /// <summary>Go: <c>TestDocumentIdentifierUnmarshalJSON</c>（proto_test.go:18-67）的第 5 组表项
    /// （`input: "42"`、`err: "expected string or object, got number"`）。</summary>
    [Fact]
    public void TestDocumentIdentifierUnmarshalJSONInvalidType()
    {
        var e = Assert.Throws<JsonException>(() => UnmarshalNew<DocumentIdentifier>("42"u8.ToArray()));
        Assert.Contains("expected string or object, got number", e.Message);
    }

    /// <summary>Go: <c>TestEnsureProgramsUnmarshalJSON</c>（proto_test.go:69-85）。</summary>
    [Fact]
    public void TestEnsureProgramsUnmarshalJSON()
    {
        var all = UnmarshalNew<EnsurePrograms>("true"u8.ToArray());
        Assert.True(all.All);

        var projects = UnmarshalNew<EnsurePrograms>("[\"/tsconfig.json\",\"/dev/null/synthetic/1\"]"u8.ToArray());
        Assert.Equal(
            new[]
            {
                new Project.ConfiguredProjectID(new Path("/tsconfig.json")).AsID(),
                Project.SyntheticProjectID.NewSyntheticProjectID(1).AsID(),
            },
            projects.Projects!);

        var e = Assert.Throws<JsonException>(() => UnmarshalNew<EnsurePrograms>("false"u8.ToArray()));
        Assert.Contains("must be true or an array", e.Message);
    }

    /// <summary>Go: <c>TestNewDiagnosticResponseIncludesFormattingContext</c>（proto_test.go:87-106）。</summary>
    [Fact]
    public void TestNewDiagnosticResponseIncludesFormattingContext()
    {
        const string text = "const 💩 = 1;";
        var file = ParseSourceFile(new SourceFileParseOptions { FileName = "/unicode.ts", Path = new Path("") }, text, ScriptKind.TS);
        var pos = text.IndexOf("=", StringComparison.Ordinal);
        Assert.True(pos > 0);
        var end = pos + 1;

        var diag = NewDiagnostic(file, NewTextRange(pos, end), Expression_expected);
        var resp = NewDiagnosticResponse(diag);

        Assert.Equal(9, resp.Pos);
        Assert.Equal(10, resp.End);
        Assert.NotNull(resp.StartPosition);
        Assert.Equal(0, resp.StartPosition.Line);
        Assert.Equal(9, (int)resp.StartPosition.Character);
        Assert.NotNull(resp.EndPosition);
        Assert.Equal(0, resp.EndPosition.Line);
        Assert.Equal(10, (int)resp.EndPosition.Character);
        Assert.Collection(
            resp.SourceLines!,
            line =>
            {
                Assert.Equal(0, line.Line);
                Assert.Equal(text, line.Text);
            });
        Assert.Equal(resp.Pos, file.GetPositionMap().UTF8ToUTF16(pos));
        Assert.Equal(resp.End, file.GetPositionMap().UTF8ToUTF16(end));
    }

    /// <summary>Go: <c>TestNewDiagnosticResponseTruncatesLongFormattingContext</c>（proto_test.go:108-123）。</summary>
    [Fact]
    public void TestNewDiagnosticResponseTruncatesLongFormattingContext()
    {
        const string text = "one\ntwo\nthree\nfour\nfive\nsix\nseven";
        var file = ParseSourceFile(new SourceFileParseOptions { FileName = "/multiline.ts", Path = new Path("") }, text, ScriptKind.TS);
        var diag = NewDiagnostic(file, NewTextRange(0, text.Length), Expression_expected);
        var resp = NewDiagnosticResponse(diag);

        Assert.NotNull(resp.StartPosition);
        Assert.Equal(0, resp.StartPosition.Line);
        Assert.Equal(0, (int)resp.StartPosition.Character);
        Assert.NotNull(resp.EndPosition);
        Assert.Equal(6, resp.EndPosition.Line);
        Assert.Equal(5, (int)resp.EndPosition.Character);
        Assert.Collection(
            resp.SourceLines!,
            line =>
            {
                Assert.Equal(0, line.Line);
                Assert.Equal("one\n", line.Text);
            },
            line =>
            {
                Assert.Equal(1, line.Line);
                Assert.Equal("two\n", line.Text);
            },
            line =>
            {
                Assert.Equal(5, line.Line);
                Assert.Equal("six\n", line.Text);
            },
            line =>
            {
                Assert.Equal(6, line.Line);
                Assert.Equal("seven", line.Text);
            });
    }
}
