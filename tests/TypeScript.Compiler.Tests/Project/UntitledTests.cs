// ported from: internal/project/untitled_test.go:1-183
//
// 与 Go 测试的差异（逐条）：
//
// 1) `if !bundled.Embedded { t.Skip(…) }`：C# 的 `Bundled.Embedded` 是 `const bool = true` ⇒ 分支不可达，不移植。
// 2) `assert.Equal(t, want, got)` → `Assert.Equal(expected, actual)`；`assert.Assert(t, cond, "…")` → `Assert.True(cond, "…")`；
//    Go 的 `_, err := session.GetLanguageService(ctx, uri); assert.NilError(t, err)` → `ProjectTestSupport.Open(...)`
//    （C# 的 Session 保留双返回，见 ProjectTests.cs 的 ProjectTestSupport）。
// 3) `context.Background()` → `Context.Background`；`projecttestutil.WithRequestID(ctx)` → `WithRequestID(ctx)`。
// 4) `lsconv.FileNameToDocumentURI`（Go 的包级函数）在 C# 里是静态类 `TypeScript.LsConv.LsConv` 的成员 ⇒ 用
//    别名 `LsConvStatic` 调用（命名空间限定拿不到静态类成员，见 docs/architecture.md §2）。
// 5) `t.Logf(...)`（只写测试日志、不做断言）→ 省略（xunit 的 `ITestOutputHelper` 没有便捷等价物；
//    本移植只保留断言）。
// 6) Go 的**未移植**部分：`TestUntitledReferences` 里 `languageService.ProvideReferences(ctx, refParams, nil)` /
//    `ProvideDefinition(ctx, uri, pos)` 的断言（属尚未落地的 M8 `ls` 切片，见对应方法的 Skip 原因）。
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Tests.ProjectTestUtil;
using static TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;

using Xunit;

using LsConvStatic = TypeScript.LsConv.LsConv;

namespace TypeScript.Tests;

/// <summary>Go: <c>TestUntitled*</c>（untitled_test.go 的全部用例）。</summary>
public class ProjectUntitledTests
{
    private const string _testContent = """
        let x = 42;

        x

        x++;
        """;

    /// <summary>Go: <c>TestUntitledReferences</c> 的前半段（untitled_test.go:18-33）：URI ↔ 文件名的往返。</summary>
    [Fact]
    public void UriRoundTrip()
    {
        var untitledURI = (DocumentUri)"untitled:Untitled-2";
        var convertedFileName = untitledURI.FileName();
        var backToUri = LsConvStatic.FileNameToDocumentURI(convertedFileName);

        Assert.Equal(untitledURI, backToUri);
        // Go 的注释：DocumentURIToFileName 产出的 untitled 文件名形如 "^/untitled/ts-nul-authority/Untitled-2"
        Assert.Equal("^/untitled/ts-nul-authority/Untitled-2", convertedFileName);
    }

    /// <summary>Go: <c>TestUntitledFileInInferredProject</c>（untitled_test.go:104-155；去掉 ProvideReferences 部分）。</summary>
    [Fact]
    public void FileInInferredProject()
    {
        // Test that untitled files are properly handled in inferred projects
        var (session, _) = Setup(new Dictionary<string, object?>());

        var ctx = WithRequestID(Context.Background);

        // Open untitled files - these should create an inferred project
        session.DidOpenFile(ctx, "untitled:Untitled-1", 1, "x\n\n", LanguageKind.TypeScript);
        session.DidOpenFile(ctx, "untitled:Untitled-2", 1, _testContent, LanguageKind.TypeScript);

        var snapshot = session.Snapshot();

        // Should have an inferred project
        Assert.True(snapshot.ProjectCollection.InferredProject() is not null);

        // Get language service for the untitled file
        var languageService = ProjectTestSupport.Open(session, ctx, (DocumentUri)"untitled:Untitled-2");

        var program = languageService.GetProgram()!;
        var untitledFileName = ((DocumentUri)"untitled:Untitled-2").FileName();
        var sourceFile = program.GetSourceFile(untitledFileName);
        Assert.NotNull(sourceFile);
        Assert.Equal(_testContent, sourceFile.Text());
    }

    /// <summary>Go: <c>TestUntitledReferences</c> 的后半段（untitled_test.go:35-102）：references/definition 断言。</summary>
    [Fact(Skip = "需要 `ls.LanguageService.ProvideReferences(ctx, refParams, nil)` 与 `ProvideDefinition(ctx, uri, pos)`"
        + "（Go 断言 3 个引用 + definition 的 Locations），属尚未落地的 M8 `ls` 切片：C# 的 Ls/ 只有 Host.cs/Project.cs/"
        + "LanguageService.cs（只含 languageservice.go 前 134 行）。同样原因，TestUntitledFileInInferredProject 的"
        + "ProvideReferences 部分（断言 4 个引用且都是 untitled: URI）也不在这里复刻。")]
    public void ReferencesAndDefinition()
    {
    }

    /// <summary>Go: <c>TestImportsInUntitled</c>（untitled_test.go:157-183）。</summary>
    [Fact]
    public void ImportsInUntitled()
    {
        var files = new Dictionary<string, object?>
        {
            // Make sure typings directory exists so it would actually try to fetch typings from this location
            [TestTypingsLocation + "/node_modules/@types/somelib/index.d.ts"] = "export const x: number;",
        };
        var (session, _) = Setup(files);
        const string content = "import \"https://deno.land/std@0.208.0/path/mod.ts\"\n\t\timport  \"./relative\"\n";
        var uri1 = (DocumentUri)"untitled:Untitled-1";
        session.DidOpenFile(Context.Background, uri1, 1, content, LanguageKind.TypeScript);

        // 2) Wait for ATA/background tasks to finish, then get a language service for the first file
        session.WaitForBackgroundTasks();
        _ = ProjectTestSupport.Open(session, Context.Background, uri1);
    }
}
