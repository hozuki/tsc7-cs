// ported from: internal/api/session_textedit_test.go:1-39
//
// 与 Go 测试的差异（逐条）：
//
// 1) 包级函数 `toAPITextEdits` 在 C# 里挂在它服务的类型上（`Session.ToAPITextEdits`，契约 §4：
//    Go 未导出函数 → internal + PascalCase），调用点写 `ApiSession.ToAPITextEdits(...)`。
// 2) `parser.ParseSourceFile(...)` → `using static TypeScript.Parser.Parser;` 后的 `ParseSourceFile(...)`
//    （仓内先例：Format/FormatTests.cs、Execute/IncrementalTests.cs）。
// 3) `tspath.Path("/app.vue")`（Go 的纯类型转换）→ C# 的 `new TSPath.Path("/app.vue")`（record struct 构造）。
// 4) `lsproto.TextEdit` → C# 的 `TypeScript.LsProto.TextEdit`（"LspTextEdit" 只是 `Session.Checker.cs` 里的
//    using 别名）；`lsproto.Position` 的 `Line`/`Character` 是 `uint`（Go 是 `uint32`）。
// 5) `assert.DeepEqual(t, edits, []*TextEdit{...})`：`TextEdit` 在 C# 是普通类（没有值相等）⇒ 逐条断言字段；
//    长度用 `Assert.Single`（Go 的期望切片长度就是 1）。
// 6) `t.Parallel()` 不移植（xunit 默认并行）。
namespace TypeScript.Tests;

using TypeScript.Api;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsProto;
using Xunit;
using static TypeScript.Parser.Parser;
using ApiSession = TypeScript.Api.Session;
using Path = TypeScript.TSPath.Path;
// `TypeScript.Api.TextEdit`（API 的 TextEdit 响应）与 `TypeScript.LsProto.TextEdit`（LSP 的 TextEdit）同名 →
// 两个命名空间都 using 时裸名会 CS0104；被测函数收的是 LSP 的那个（Session.Checker.cs 里同样用别名）。
using LspTextEdit = TypeScript.LsProto.TextEdit;

/// <summary>Go: <c>internal/api/session_textedit_test.go</c> 的 <c>TestToAPITextEditsUsesOriginalCoordinates</c>（session_textedit_test.go:14）。</summary>
public class SessionTextEditTests
{
    /// <summary>Go: <c>TestToAPITextEditsUsesOriginalCoordinates</c>（session_textedit_test.go:14-39）。</summary>
    [Fact]
    public void TestToAPITextEditsUsesOriginalCoordinates()
    {
        var sourceFile = ParseSourceFile(
            new SourceFileParseOptions { FileName = "/app.vue", Path = new Path("/app.vue") },
            "const transformed = true;",
            ScriptKind.TS);
        sourceFile.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            OriginalText = "😀\nabc",
            ContentMapper = "mapper",
        });

        var edits = ApiSession.ToAPITextEdits(sourceFile, [new LspTextEdit
        {
            Range = new Range
            {
                Start = new Position { Line = 1, Character = 1 },
                End = new Position { Line = 1, Character = 2 },
            },
            NewText = "x",
        }]);

        var edit = Assert.Single(edits!);
        Assert.Equal(4, edit.Pos);
        Assert.Equal(5, edit.End);
        Assert.Equal("x", edit.NewText);
    }
}
