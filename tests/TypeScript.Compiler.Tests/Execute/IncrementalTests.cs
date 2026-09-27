// ported from: internal/execute/incremental/external_diagnostic_test.go
//
// 与 Go 的偏离（都是机械替换，不改变断言强度）：
// 1) Go 测试在 `package incremental` 里，因此能直接调未导出的 `astDiagToBuildInfoDiag` 并读
//    `serialized.source`/`serialized.messageText` → C# 里对应 `Incremental.AstDiagToBuildInfoDiag`（internal
//    static，ProgramToSnapshot.cs:575）与 `BuildInfoDiagnosticWithFileName`（internal 类型，Snapshot.cs:122）的
//    internal 字段 `_source`/`_messageText`；测试程序集有 InternalsVisibleTo("TypeScript.Compiler.Tests")。
// 2) Go 的 `buildInfoDiagnosticWithFileName.toDiagnostic(p *compiler.Program, file)` → C# 的
//    `ToDiagnostic(CompilerProgram p, SourceFile? file)`；Go 传 nil，C# 参数非空 → `null!`（与 Go 一样在其内部
//    走的是 `_file` 为空且 `_noFile` 为假的分支，不会解引用 p）。
// 3) `parser.ParseSourceFile(ast.SourceFileParseOptions{…}, "", core.ScriptKindTS)` →
//    `ParseSourceFile(new SourceFileParseOptions { FileName = …, Path = new Path(…) }, "", ScriptKind.TS)`
//    （SourceFileParseOptions 在 C# 是 class，用对象初始化器；`using static TypeScript.Parser.Parser;`）。
// 4) `ast.NewExternalDiagnostic` / `core.NewTextRange` / `diagnostics.CategoryWarning` / `locale.Default` →
//    `Ast.NewExternalDiagnostic` / `Core.NewTextRange` / `Category.Warning` / `Locale.Default`
//    （包级函数用 `using static` 后与 Go 同形；`Category` 是 `TypeScript.Diagnostics` 的枚举）。
// 5) Go 的 `gotest.tools/assert` → xunit 的 `Assert.Equal`（逐条对应）。
// 6) Go 的 `t.Parallel()` 由 xunit 调度，不逐字搬。
//
// 命名空间：用 `TypeScript.Tests.Execute`（镜像 Execute 目录；先例 tests/TypeScript.Compiler.Tests/Printer/UtilitiesTests.cs 的
// `TypeScript.Tests.Printer`）。本切片的验收命令是 `--filter "FullyQualifiedName~Execute"`，而目录名不进 FQN，
// 只有命名空间带 "Execute" 才会被它选中。
// 未移植：无（external_diagnostic_test.go 只有这一个测试）。
namespace TypeScript.Tests.Execute;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Locale;
using Xunit;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.Parser.Parser;
using Path = TypeScript.TSPath.Path;

public class IncrementalTests
{
    // Go: TestExternalDiagnosticBuildInfoRoundTrip（external_diagnostic_test.go:13-27）。
    [Fact]
    public void TestExternalDiagnosticBuildInfoRoundTrip()
    {
        var file = ParseSourceFile(new SourceFileParseOptions { FileName = "/app.vue", Path = new Path("/app.vue") }, "", ScriptKind.TS);
        var diagnostic = NewExternalDiagnostic(file, NewTextRange(1, 2), "vue", Category.Warning, 1001, "mapper warning");

        var serialized = AstDiagToBuildInfoDiag(diagnostic);
        Assert.Equal("vue", serialized._source);
        Assert.Equal("mapper warning", serialized._messageText);

        var restored = serialized.ToDiagnostic(null!, file);
        Assert.Equal("vue", restored.Source());
        Assert.Equal("mapper warning", restored.Localize(Locale.Default));
    }
}
