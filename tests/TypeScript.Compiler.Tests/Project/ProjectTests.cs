// ported from: internal/project/project_test.go:1-957
//
// 覆盖 Go 的 5 个顶层测试（TestProjectProgramUpdateKind / TestProject / TestPushDiagnostics / TestDisplayName /
// TestProgressNotifications）。Go 的 `t.Run` 子测试 → 独立的 [Fact] 方法（方法名 = 子测试名的 PascalCase 转写），
// 与仓内既有移植（如 AtaTests）一致。
//
// 与 Go 测试的差异（逐条）：
//
// 1) `if !bundled.Embedded { t.Skip("bundled files are not embedded") }`：C# 的 `Bundled.Embedded` 是
//    `const bool Embedded = true`（Bundled.cs 文件头：108 个 .d.ts 始终作为 EmbeddedResource 发布，没有
//    noembed 分支），所以这个 Skip 分支在 C# 里不可达 → 不移植（每个测试都真的跑）。
// 2) `assert.Equal(t, want, got)` → `Assert.Equal(expected, actual)`（xunit 的顺序是 (expected, actual)，
//    调用点已按此重排）；`assert.Assert(t, cond)` → `Assert.True(cond)`；Go 的 `_, err := …` + `assert.NilError`
//    在 C# 侧：`Session.GetLanguageService` 按 ProjHub 的契约**保留双返回** `(Ls.LanguageService?, Exception?)`
//    （error 用 Exception 承载、不抛）⇒ 用 `ProjectTestSupport.Open(session, ctx, uri)`（断言 error 为 null 并取出
//    LanguageService）；`assert.ErrorContains(err, "no project found")` → `ProjectTestSupport.AssertNoProjectError`。
//    `Assert.True(x is not null, msg)` 后面还要解引用 `x` 的地方写成 `Assert.NotNull(x)`（xunit 的 NotNull 带
//    [NotNull] 注解，可让可空流分析通过，避免 CS8602 在 TreatWarningsAsErrors 下变错）。
// 3) `context.Background()` → `Context.Background`；`projecttestutil.WithRequestID(ctx)` →
//    `WithRequestID(ctx)`。Go 的 `t.Context()`（测试结束时取消）在 xunit 里没有对应物，
//    这里用 `Context.Background` 顶替（这些用例都不依赖取消）。
// 4) `files` 的取值 `files["/src/index.ts"].(string)` → `(string)files["/src/index.ts"]!`（脚手架的文件树是
//    `Dictionary<string, object?>`，见 cs 文件头第 2 条）。
// 5) `tspath.Path(...)` → 别名 `Path`（`System.IO.Path` 经 ImplicitUsings 引入，必须用同名别名消歧，
//    仓内先例 WatchTests.cs）。裸写 `Project` 会命中命名空间 `TypeScript.Project`（CS0118）⇒ 本文件里
//    `Project`/`Client`/`Session` 等类型都靠 `var` 推断，没有一处需要写类型名 `Project`（若将来需要，
//    用异名别名，仓内先例 Execute/Tsc.cs 的 `CmProject`：与命名空间同名的 `using Project = …` 无效）。
// 6) `diagnostics.Type_0_is_not_assignable_to_type_1.Code()` → 裸名 `Type_0_is_not_assignable_to_type_1.Code()`
//    （`using static TypeScript.Diagnostics.Diagnostics;`，与 Session.cs 等既有调用点一致：Go 的包名在 C# 是
//    **命名空间**，`Diagnostics.Project_0` 这种写法定不到静态类成员，见 docs/architecture.md §2）。
// 7) `diag.Message.AsString()`（lsproto.StringOrMarkupContent 的访问器）→ C# 生成物里是字段 `String`
//    （Lsp.Generated.Unions.cs），故写 `diag!.Message.String!`。
// 8) Go 的**未移植**子测试（原因逐条在对应方法上）：
//    · `query globals before/after semantic checking`（两个 baseline 驱动用例：需要 `ls.ProvideHover` +
//      `ls.ProvideDiagnostics` + `baseline.Run` 的 jsonc 基线 + 完整 checker）。
//    `publishes global diagnostics after checking` 里**只用** `ProvideDiagnostics` 的那段子断言（报告里不含
//    "Cannot find global"、含 code 2550）同样省略，其余断言照 Go 保留（见该方法内的说明）。
using System.Linq;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Project;
using TypeScript.Tests.ProjectTestUtil;
using static TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;
using TypeScript.TSPath;

using Xunit;

using Path = TypeScript.TSPath.Path;
using Range = TypeScript.LsProto.Range;

using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Project.ProgramUpdateKind;

namespace TypeScript.Tests;

/// <summary>Go 的 <c>publishDiagnosticsCall</c> 与 <c>filterDiagnosticsByURI</c>（project_test.go:24-36）。</summary>
internal static class ProjectTestSupport
{
    /// <summary>Go: <c>func filterDiagnosticsByURI(calls, uri, from) []publishDiagnosticsCall</c>。</summary>
    internal static (Context Ctx, PublishDiagnosticsParams Params)[] FilterDiagnosticsByUri(
        (Context Ctx, PublishDiagnosticsParams Params)[] calls,
        DocumentUri uri,
        int from)
    {
        var result = new List<(Context Ctx, PublishDiagnosticsParams Params)>();
        for (var i = from; i < calls.Length; i++)
        {
            if (calls[i].Params.Uri == uri)
            {
                result.Add(calls[i]);
            }
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>calls[i].Params.Uri == "file:///…"</c>（DocumentUri 的 == 是序数比较）。</summary>
    internal static int IndexOfLastCallFor(
        (Context Ctx, PublishDiagnosticsParams Params)[] calls,
        DocumentUri uri)
    {
        for (var i = calls.Length - 1; i >= 0; i--)
        {
            if (calls[i].Params.Uri == uri)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Go 的 `ls, err := session.GetLanguageService(ctx, uri); assert.NilError(t, err)`。C# 的
    /// `Session.GetLanguageService` 按 ProjHub 的契约**保留双返回**（`(Ls.LanguageService?, Exception?)`，
    /// error 用 Exception 承载、不抛），所以这里断言无错并取出 LanguageService。
    /// </summary>
    internal static Ls.LanguageService Open(Session session, Context ctx, DocumentUri uri)
    {
        var (languageService, error) = session.GetLanguageService(ctx, uri);
        Assert.Null(error);
        return languageService!;
    }

    /// <summary>Go 的 `assert.ErrorContains(t, err, "no project found")`。</summary>
    internal static void AssertNoProjectError(Session session, Context ctx, DocumentUri uri)
    {
        var (_, error) = session.GetLanguageService(ctx, uri);
        Assert.NotNull(error);
        Assert.Contains("no project found", error!.Message);
    }
}

/// <summary>Go: <c>TestProjectProgramUpdateKind</c>（project_test.go:41-238）。</summary>
public class ProjectProgramUpdateKindTests
{
    /// <summary>Go: <c>TestProjectProgramUpdateKind/NewFiles on initial build</c>。</summary>
    [Fact]
    public void NewFilesOnInitialBuild()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = "{}",
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, _) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        var snapshot = session.Snapshot();
        var configured = snapshot.ProjectCollection.ConfiguredProject(new Path("/src/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal(ProgramUpdateKindNewFiles, configured.ProgramUpdateKind);
    }

    /// <summary>Go: <c>TestProjectProgramUpdateKind/Cloned on single-file change</c>。</summary>
    [Fact]
    public void ClonedOnSingleFileChange()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = "{}",
            ["/src/index.ts"] = "console.log('Hello');",
        };
        var (session, _) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.DidChangeFile(Context.Background, "file:///src/index.ts", 2,
        [
            new TextDocumentContentChangePartialOrWholeDocument
            {
                Partial = new TextDocumentContentChangePartial
                {
                    Text = "\n",
                    Range = new Range
                    {
                        Start = new Position { Line = 0, Character = 20 },
                        End = new Position { Line = 0, Character = 20 },
                    },
                },
            },
        ]);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        var snapshot = session.Snapshot();
        var configured = snapshot.ProjectCollection.ConfiguredProject(new Path("/src/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal(ProgramUpdateKindCloned, configured.ProgramUpdateKind);
    }

    /// <summary>Go: <c>TestProjectProgramUpdateKind/compiler options update inferred project</c>。</summary>
    [Fact]
    public void CompilerOptionsUpdateInferredProject()
    {
        const string fileName = "/src/index.ts";
        var (session, _) = Setup(new Dictionary<string, object?>
        {
            [fileName] = "export const x = 1;",
        });
        var uri = (DocumentUri)("file://" + fileName);
        session.DidOpenFile(Context.Background, uri, 1, "export const x = 1;", LanguageKind.TypeScript);
        var oldProject = session.Snapshot().ProjectCollection.InferredProject();
        Assert.NotNull(oldProject);
        var oldProgram = oldProject.Program!;
        Assert.Equal(Tristate.TSUnknown, oldProgram.Options().Strict);

        session.DidChangeCompilerOptionsForInferredProjects(Context.Background, new CompilerOptions
        {
            NoLib = Tristate.TSTrue,
            Strict = Tristate.TSTrue,
        });
        ProjectTestSupport.Open(session, Context.Background, uri);

        var updatedProject = session.Snapshot().ProjectCollection.InferredProject();
        Assert.NotNull(updatedProject);
        Assert.Equal(Tristate.TSTrue, updatedProject.CommandLine!.CompilerOptions()!.Strict);
        Assert.NotSame(oldProgram, updatedProject.Program);
        Assert.Equal(Tristate.TSTrue, updatedProject.Program!.Options().Strict);
        Assert.Equal(Tristate.TSUnknown, oldProgram.Options().Strict);
    }

    /// <summary>Go: <c>TestProjectProgramUpdateKind/NewFiles when import resolution mode changes</c>。</summary>
    [Fact]
    public void NewFilesWhenImportResolutionModeChanges()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """
                {
                				"compilerOptions":{"module":"preserve","moduleResolution":"bundler","noEmit":true},
                				"files":["index.ts"]
                			}
                """,
            ["/src/index.ts"] = """
                import type { Value } from "pkg" with { "resolution-mode": "require" };
                const value: Value = { mode: "require" };
                """,
            ["/src/node_modules/pkg/package.json"] = """
                {
                				"name": "pkg",
                				"version": "1.0.0",
                				"exports": {
                					".": {
                						"import": "./index.mjs",
                						"require": "./index.js"
                					}
                				}
                			}
                """,
            ["/src/node_modules/pkg/index.d.mts"] = """export interface Value { mode: "import" }""",
            ["/src/node_modules/pkg/index.d.ts"] = """export interface Value { mode: "require" }""",
        };
        var (session, _) = Setup(files);
        var uri = (DocumentUri)"file:///src/index.ts";
        session.DidOpenFile(Context.Background, uri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        var ls = ProjectTestSupport.Open(session, Context.Background, uri);
        var program = ls.GetProgram()!;
        Assert.Empty(program.GetSemanticDiagnostics(WithRequestID(Context.Background), program.GetSourceFile("/src/index.ts")));

        session.DidChangeFile(Context.Background, uri, 2,
        [
            new TextDocumentContentChangePartialOrWholeDocument
            {
                WholeDocument = new TextDocumentContentChangeWholeDocument
                {
                    Text = """
                        import type { Value } from "pkg" with { "resolution-mode": "import" };
                        const value: Value = { mode: "require" };
                        """,
                },
            },
        ]);
        ls = ProjectTestSupport.Open(session, Context.Background, uri);
        program = ls.GetProgram()!;
        var diags = program.GetSemanticDiagnostics(WithRequestID(Context.Background), program.GetSourceFile("/src/index.ts"));
        Assert.Single(diags);
        Assert.Equal(Type_0_is_not_assignable_to_type_1.Code(), diags[0].Code());

        var configured = session.Snapshot().ProjectCollection.ConfiguredProject(new Path("/src/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal(ProgramUpdateKindNewFiles, configured.ProgramUpdateKind);
    }

    /// <summary>Go: <c>TestProjectProgramUpdateKind/SameFileNames on config change without root changes</c>。</summary>
    [Fact]
    public void SameFileNamesOnConfigChangeWithoutRootChanges()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """{"compilerOptions": {"strict": true}}""",
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        utils.FS().WriteFile("/src/tsconfig.json", """{"compilerOptions": {"strict": false}}""");
        session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///src/tsconfig.json", Type = FileChangeType.Changed }]);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        var snapshot = session.Snapshot();
        var configured = snapshot.ProjectCollection.ConfiguredProject(new Path("/src/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal(ProgramUpdateKindSameFileNames, configured.ProgramUpdateKind);
    }

    /// <summary>Go: <c>TestProjectProgramUpdateKind/NewFiles on root addition</c>。</summary>
    [Fact]
    public void NewFilesOnRootAddition()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = "{}",
            ["/src/index.ts"] = "export {}",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        const string content = "export const y = 2;";
        utils.FS().WriteFile("/src/newfile.ts", content);
        session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///src/newfile.ts", Type = FileChangeType.Created }]);
        session.DidOpenFile(Context.Background, "file:///src/newfile.ts", 1, content, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/newfile.ts");
        var snapshot = session.Snapshot();
        var configured = snapshot.ProjectCollection.ConfiguredProject(new Path("/src/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal(ProgramUpdateKindNewFiles, configured.ProgramUpdateKind);
    }

    /// <summary>Go: <c>TestProjectProgramUpdateKind/SameFileNames when adding an unresolvable import with multi-file change</c>。</summary>
    [Fact]
    public void SameFileNamesWhenAddingUnresolvableImportWithMultiFileChange()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = "{}",
            ["/src/index.ts"] = "export const x = 1;",
            ["/src/other.ts"] = "export const z = 3;",
        };
        var (session, _) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        // Change index.ts to add an unresolvable import
        session.DidChangeFile(Context.Background, "file:///src/index.ts", 2,
        [
            new TextDocumentContentChangePartialOrWholeDocument
            {
                Partial = new TextDocumentContentChangePartial
                {
                    Text = "\nimport \"./does-not-exist\";\n",
                    Range = new Range
                    {
                        Start = new Position { Line = 0, Character = 0 },
                        End = new Position { Line = 0, Character = 0 },
                    },
                },
            },
        ]);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        var snapshot = session.Snapshot();
        var configured = snapshot.ProjectCollection.ConfiguredProject(new Path("/src/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal(ProgramUpdateKindSameFileNames, configured.ProgramUpdateKind);
    }
}

/// <summary>Go: <c>TestProject</c>（project_test.go:240-362）。</summary>
public class ProjectTests
{
    private static TypingsInstallerOptions JqueryTypings() => new()
    {
        PackageToFile = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Provide typings content to be installed for jquery so ATA actually installs something
            ["jquery"] = """declare const $: { x: number }""",
        },
    };

    /// <summary>Go: <c>TestProject/commandLineWithTypingsFiles is reset on CommandLine change</c>。</summary>
    [Fact]
    public void CommandLineWithTypingsFilesIsResetOnCommandLineChange()
    {
        var files = new Dictionary<string, object?>
        {
            ["/user/username/projects/project1/app.js"] = "",
            ["/user/username/projects/project1/package.json"] = """{"name":"p1","dependencies":{"jquery":"^3.1.0"}}""",
            ["/user/username/projects/project2/app.js"] = "",
        };

        var (session, utils) = SetupWithTypingsInstaller(files, JqueryTypings());

        // 1) Open an inferred project file that triggers ATA
        var uri1 = (DocumentUri)"file:///user/username/projects/project1/app.js";
        session.DidOpenFile(Context.Background, uri1, 1, (string)files["/user/username/projects/project1/app.js"]!, LanguageKind.JavaScript);

        // 2) Wait for ATA/background tasks to finish, then get a language service for the first file
        session.WaitForBackgroundTasks();
        // Sanity check: ensure ATA performed at least one install
        var npmCalls = utils.NpmExecutor().NpmInstallCalls();
        Assert.True(npmCalls.Length > 0, "expected at least one npm install call from ATA");
        ProjectTestSupport.Open(session, Context.Background, uri1);

        // 3) Open another inferred project file
        var uri2 = (DocumentUri)"file:///user/username/projects/project2/app.js";
        session.DidOpenFile(Context.Background, uri2, 1, "", LanguageKind.JavaScript);

        // 4) Get a language service for the second file
        //    If commandLineWithTypingsFiles was not reset, the new program command line
        //    won't include the newly opened file and this will fail.
        ProjectTestSupport.Open(session, Context.Background, uri2);
    }

    /// <summary>Go: <c>TestProject/inferred project rebuilt twice in one snapshot after typings install does not crash</c>。</summary>
    [Fact]
    public void InferredProjectRebuiltTwiceInOneSnapshotAfterTypingsInstallDoesNotCrash()
    {
        // Reproduces the nil-command-line crash in (*Program).SingleThreaded (issue #3873)（Go 用例的注释见
        // project_test.go:267-286）。Go 用 `defer func(){ recover() }` 把 panic 转成 t.Fatalf；C# 里对应的是
        // 异常（§3.2），异常会直接让 xunit 用例失败 ⇒ 不需要 recover 分支。
        var files = new Dictionary<string, object?>
        {
            ["/user/username/projects/project1/a.js"] = "",
            ["/user/username/projects/project1/package.json"] = """{"name":"p1","dependencies":{"jquery":"^3.1.0"}}""",
            ["/user/username/projects/project1/b.ts"] = "export const y = 1;",
        };

        var (session, utils) = SetupWithTypingsInstaller(files, JqueryTypings());

        var aURI = (DocumentUri)"file:///user/username/projects/project1/a.js";
        var bURI = (DocumentUri)"file:///user/username/projects/project1/b.ts";

        // 1) Open the file that triggers ATA, plus another file that joins the inferred project.
        session.DidOpenFile(Context.Background, aURI, 1, "", LanguageKind.JavaScript);
        session.DidOpenFile(Context.Background, bURI, 1, (string)files["/user/username/projects/project1/b.ts"]!, LanguageKind.TypeScript);

        // 2) Let ATA install jquery typings, then build the inferred program so its
        //    typings files are populated.
        session.WaitForBackgroundTasks();
        var npmCalls = utils.NpmExecutor().NpmInstallCalls();
        Assert.True(npmCalls.Length > 0, "expected at least one npm install call from ATA");
        ProjectTestSupport.Open(session, Context.Background, aURI);

        // 3) Queue two pending changes that will be flushed together in the next snapshot.
        session.DidChangeFile(Context.Background, aURI, 2,
        [
            new TextDocumentContentChangePartialOrWholeDocument
            {
                WholeDocument = new TextDocumentContentChangeWholeDocument { Text = "// changed" },
            },
        ]);
        utils.FS().WriteFile("/user/username/projects/project1/tsconfig.json", """{"compilerOptions":{},"files":["b.ts"]}""");
        session.DidChangeWatchedFiles(Context.Background,
        [
            new FileEvent
            {
                Type = FileChangeType.Created,
                Uri = "file:///user/username/projects/project1/tsconfig.json",
            },
        ]);

        // 4) Flush the pending changes and the request in a single snapshot.
        ProjectTestSupport.Open(session, Context.Background, aURI);
    }
}

/// <summary>Go: <c>TestPushDiagnostics</c>（project_test.go:364-780）。</summary>
public class ProjectPushDiagnosticsTests
{
    /// <summary>Go: <c>TestPushDiagnostics/publishes program diagnostics on initial program creation</c>。</summary>
    [Fact]
    public void PublishesProgramDiagnosticsOnInitialProgramCreation()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """{"compilerOptions": {"baseUrl": "."}}""",
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");

        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        Assert.True(calls.Length > 0, "expected at least one PublishDiagnostics call");

        // Find the call for tsconfig.json
        var tsconfigCall = calls.FirstOrDefault(call => call.Params.Uri == (DocumentUri)"file:///src/tsconfig.json");
        Assert.NotNull(tsconfigCall.Params);
        Assert.True(tsconfigCall.Params.Diagnostics!.Length > 0, "expected at least one diagnostic");
    }

    /// <summary>Go: <c>TestPushDiagnostics/publishes config parsing diagnostics on initial program creation</c>。</summary>
    [Fact]
    public void PublishesConfigParsingDiagnosticsOnInitialProgramCreation()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """
                {
                				"compilerOptions": {
                					"target": "nope"
                				}
                			}
                """,
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");

        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        var tsconfigCalls = ProjectTestSupport.FilterDiagnosticsByUri(calls, "file:///src/tsconfig.json", 0);
        Assert.True(tsconfigCalls.Length > 0, "expected PublishDiagnostics call for tsconfig.json");
        var lastTsconfigCall = tsconfigCalls[^1];

        const string expectedMessage = "Argument for '--target' option must be:";
        Assert.True(
            lastTsconfigCall.Params.Diagnostics!.Any(diag => diag!.Message.String!.Contains(expectedMessage, StringComparison.Ordinal)),
            $"expected invalid target diagnostic on tsconfig.json, got: {string.Join(", ", lastTsconfigCall.Params.Diagnostics!.Select(d => d!.Message.String))}");
    }

    /// <summary>Go: <c>TestPushDiagnostics/clears diagnostics when project is removed</c>。</summary>
    [Fact]
    public void ClearsDiagnosticsWhenProjectIsRemoved()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """{"compilerOptions": {"baseUrl": "."}}""",
            ["/src/index.ts"] = "export const x = 1;",
            ["/src2/tsconfig.json"] = """{"compilerOptions": {}}""",
            ["/src2/index.ts"] = "export const y = 2;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.WaitForBackgroundTasks();

        // Open a file in a different project to trigger cleanup of the first
        session.DidCloseFile(Context.Background, "file:///src/index.ts");
        session.DidOpenFile(Context.Background, "file:///src2/index.ts", 1, (string)files["/src2/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src2/index.ts");
        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        // Should have at least one call for the first project with diagnostics,
        // and one clearing it after switching projects
        var firstProjectCalls = ProjectTestSupport.FilterDiagnosticsByUri(calls, "file:///src/tsconfig.json", 0);
        Assert.True(firstProjectCalls.Length >= 2, "expected at least 2 PublishDiagnostics calls for first project");
        // Last call should clear diagnostics
        var lastCall = firstProjectCalls[^1];
        Assert.Empty(lastCall.Params.Diagnostics!);
    }

    /// <summary>Go: <c>TestPushDiagnostics/updates diagnostics when program changes</c>。</summary>
    [Fact]
    public void UpdatesDiagnosticsWhenProgramChanges()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """{"compilerOptions": {"baseUrl": "."}}""",
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.WaitForBackgroundTasks();

        var initialCallCount = utils.Client().PublishDiagnosticsCalls().Length;

        // Change the tsconfig to remove baseUrl
        utils.FS().WriteFile("/src/tsconfig.json", """{"compilerOptions": {}}""");
        session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///src/tsconfig.json", Type = FileChangeType.Changed }]);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        Assert.True(calls.Length > initialCallCount, "expected additional PublishDiagnostics call after change");

        // Find the last call for tsconfig.json
        var lastIndex = ProjectTestSupport.IndexOfLastCallFor(calls, "file:///src/tsconfig.json");
        Assert.True(lastIndex >= 0, "expected PublishDiagnostics call for tsconfig.json");
        // After fixing the error, there should be no program diagnostics
        Assert.Empty(calls[lastIndex].Params.Diagnostics!);
    }

    /// <summary>Go: <c>TestPushDiagnostics/updates diagnostics when a config file changes on disk with no follow-up request</c>。</summary>
    [Fact]
    public void UpdatesDiagnosticsWhenConfigFileChangesOnDiskWithNoFollowUpRequest()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """{"compilerOptions": {}}""",
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.WaitForBackgroundTasks();

        var callsBeforeChange = utils.Client().PublishDiagnosticsCalls().Length;

        // Editors do not attach the language server to JSON documents, so a config file
        // edit only reaches the session through the file watcher.
        utils.FS().WriteFile("/src/tsconfig.json", """{"compilerOptions": {"target": "nope"}}""");
        session.DidChangeWatchedFiles(Context.Background,
        [
            new FileEvent { Uri = "file:///src/tsconfig.json", Type = FileChangeType.Changed },
        ]);
        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        var tsconfigCalls = ProjectTestSupport.FilterDiagnosticsByUri(calls, "file:///src/tsconfig.json", callsBeforeChange);
        Assert.True(tsconfigCalls.Length > 0, "expected PublishDiagnostics call for tsconfig.json after watched file change");
        var lastTsconfigCall = tsconfigCalls[^1];

        const string expectedMessage = "Argument for '--target' option must be:";
        Assert.True(
            lastTsconfigCall.Params.Diagnostics!.Any(diag => diag!.Message.String!.Contains(expectedMessage, StringComparison.Ordinal)),
            $"expected invalid target diagnostic on tsconfig.json, got: {string.Join(", ", lastTsconfigCall.Params.Diagnostics!.Select(d => d!.Message.String))}");
    }

    /// <summary>Go: <c>TestPushDiagnostics/does not publish for inferred projects</c>。</summary>
    [Fact]
    public void DoesNotPublishForInferredProjects()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/index.ts"] = "let x: number = 'not a number';",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        // Should not have any calls since inferred projects don't have tsconfig.json
        Assert.Empty(calls);
    }

    /// <summary>Go: <c>TestPushDiagnostics/does not publish when validation is disabled</c>。</summary>
    [Fact]
    public void DoesNotPublishWhenValidationIsDisabled()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """
                {
                				"compilerOptions": {
                					"target": "nope"
                				}
                			}
                """,
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        var prefs = TypeScript.LsUtil.LsUtil.NewDefaultUserPreferences();
        prefs.EnableValidation = Tristate.TSFalse;
        session.Configure(prefs);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        foreach (var call in calls)
        {
            Assert.Empty(call.Params.Diagnostics!);
        }
    }

    /// <summary>Go: <c>TestPushDiagnostics/clears diagnostics when validation is disabled</c>。</summary>
    [Fact]
    public void ClearsDiagnosticsWhenValidationIsDisabled()
    {
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """
                {
                				"compilerOptions": {
                					"target": "nope"
                				}
                			}
                """,
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///src/index.ts", 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        var tsconfigCalls = ProjectTestSupport.FilterDiagnosticsByUri(calls, "file:///src/tsconfig.json", 0);
        Assert.True(tsconfigCalls.Length > 0, "expected initial PublishDiagnostics call for tsconfig.json");
        Assert.True(tsconfigCalls[^1].Params.Diagnostics!.Length > 0, "expected initial diagnostics");

        var prefs = TypeScript.LsUtil.LsUtil.NewDefaultUserPreferences();
        prefs.EnableValidation = Tristate.TSFalse;
        session.Configure(prefs);
        ProjectTestSupport.Open(session, Context.Background, "file:///src/index.ts");
        session.WaitForBackgroundTasks();

        calls = utils.Client().PublishDiagnosticsCalls();
        tsconfigCalls = ProjectTestSupport.FilterDiagnosticsByUri(calls, "file:///src/tsconfig.json", calls.Length - 1);
        if (tsconfigCalls.Length == 0)
        {
            tsconfigCalls = ProjectTestSupport.FilterDiagnosticsByUri(calls, "file:///src/tsconfig.json", 0);
        }

        var lastTsconfigCall = tsconfigCalls[^1];
        Assert.Empty(lastTsconfigCall.Params.Diagnostics!);
    }

    /// <summary>Go: <c>TestPushDiagnostics/publishes global diagnostics after checking</c>。</summary>
    [Fact]
    public void PublishesGlobalDiagnosticsAfterChecking()
    {
        // Use a target/lib that does not include Disposable, then write code that needs it.
        // This triggers a deferred "Cannot find global type 'Disposable'" global diagnostic
        // during checking, which should be accumulated and published on the tsconfig URI.
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.json"] = """
                {
                				"compilerOptions": {
                					"target": "es2020"
                				}
                			}
                """,
            ["/src/index.ts"] = "export function f() {\n\t\t\t\tusing x = { [Symbol.dispose]() {} };\n\t\t\t}",
        };
        var (session, utils) = Setup(files);
        var uri = (DocumentUri)"file:///src/index.ts";
        session.DidOpenFile(Context.Background, uri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
        // Request semantic diagnostics to trigger checking, which triggers the global type resolvers.
        var ls = ProjectTestSupport.Open(session, WithRequestID(Context.Background), uri);
        // Drain background tasks from DidOpenFile (publishProgramDiagnostics, etc.)
        // before triggering global diagnostics, to avoid racing with publishGlobalDiagnostics.
        session.WaitForBackgroundTasks();

        for (var i = 0; i < 2; i++)
        {
            var program = ls.GetProgram()!;
            var file = program.GetSourceFile("/src/index.ts");
            var diags = program.GetSemanticDiagnostics(
                TypeScript.Core.Core.WithCheckerLifetime(WithRequestID(Context.Background), CheckerLifetime.Diagnostics),
                file);
            Assert.True(diags.Length > 0);
            foreach (var diag in diags)
            {
                Assert.Same(file, diag.File());
            }

            // ⚠ Go 在这里还调 `ls.ProvideDiagnostics(core.WithCheckerLifetime(…), uri)` 并断言
            // `FullDocumentDiagnosticReport.Items` 里没有 "Cannot find global"、且含 code 2550（Symbol.dispose）
            // ——`ProvideDiagnostics` 属尚未落地的 M8 `ls` 切片（见文件头第 8 条）；本用例把这段省略，
            // 其余断言（下面的 EnqueuePublishGlobalDiagnostics + tsconfig 的全局诊断）照 Go 保留。
        }

        // Enqueue global diagnostics publishing (normally done by the LSP server after each request).
        session.EnqueuePublishGlobalDiagnostics();
        session.WaitForBackgroundTasks();

        var calls = utils.Client().PublishDiagnosticsCalls();
        // Find the last call for tsconfig.json
        var lastIndex = ProjectTestSupport.IndexOfLastCallFor(calls, "file:///src/tsconfig.json");
        Assert.True(lastIndex >= 0, "expected PublishDiagnostics call for tsconfig.json");
        // Should have global diagnostics (e.g., Cannot find global type 'Disposable')
        Assert.True(
            calls[lastIndex].Params.Diagnostics!.Any(diag => diag!.Message.String!.Contains("Cannot find global", StringComparison.Ordinal)),
            $"expected a 'Cannot find global' diagnostic on tsconfig.json, got: {string.Join(", ", calls[lastIndex].Params.Diagnostics!.Select(d => d!.Message.String))}");
    }

    /// <summary>Go: <c>TestPushDiagnostics/query globals before/after semantic checking</c>（两个 baseline 驱动的用例）。</summary>
    [Fact(Skip = "baseline 驱动：Go 用 `baseline.Run(t, …+\".jsonc\", …, baseline.Options{Subfolder: \"project\"})` 写 "
        + "baselines/{local,reference}/project/*.jsonc，并调用 `ls.ProvideHover` + `ls.ProvideDiagnostics` + "
        + "`core.CheckerLifetimeDiagnostics` 记录 checker 状态差异。C# 侧的 baseline 基础设施（TestUtil/Baseline）与 "
        + "ls 切片的 ProvideHover/ProvideDiagnostics 都还没落地，且新移植没有对应的 reference baseline 可写 → 跳过。")]
    public void QueryGlobalsBeforeAfterSemanticChecking()
    {
    }
}

/// <summary>Go: <c>TestDisplayName</c>（project_test.go:782-855）。</summary>
public class ProjectDisplayNameTests
{
    /// <summary>Go: <c>TestDisplayName/configured project returns relative config path</c>。</summary>
    [Fact]
    public void ConfiguredProjectReturnsRelativeConfigPath()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/tsconfig.json"] = "{}",
            ["/home/projects/index.ts"] = "export const x = 1;",
        };
        var (session, _) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///home/projects/index.ts", 1, "export const x = 1;", LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///home/projects/index.ts");

        var snapshot = session.Snapshot();
        var configured = snapshot.ProjectCollection.ConfiguredProject(new Path("/home/projects/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal("tsconfig.json", configured.DisplayName("/home/projects"));
    }

    /// <summary>Go: <c>TestDisplayName/configured project with nested config</c>。</summary>
    [Fact]
    public void ConfiguredProjectWithNestedConfig()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/sub/tsconfig.json"] = "{}",
            ["/home/projects/sub/index.ts"] = "export const x = 1;",
        };
        var (session, _) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///home/projects/sub/index.ts", 1, "export const x = 1;", LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///home/projects/sub/index.ts");

        var snapshot = session.Snapshot();
        var configured = snapshot.ProjectCollection.ConfiguredProject(new Path("/home/projects/sub/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal("sub/tsconfig.json", configured.DisplayName("/home/projects"));
    }

    /// <summary>Go: <c>TestDisplayName/configured project preserves config path casing</c>。</summary>
    [Fact]
    public void ConfiguredProjectPreservesConfigPathCasing()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/Project/tsconfig.json"] = "{}",
            ["/home/projects/Project/index.ts"] = "export const x = 1;",
        };
        var (session, _) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///home/projects/Project/index.ts", 1, "export const x = 1;", LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///home/projects/Project/index.ts");

        var configured = session.Snapshot().ProjectCollection.ConfiguredProject(new Path("/home/projects/project/tsconfig.json"));
        Assert.NotNull(configured);
        Assert.Equal("Project/tsconfig.json", configured.DisplayName("/home/projects"));
    }

    /// <summary>Go: <c>TestDisplayName/inferred project returns directory base name</c>。</summary>
    [Fact]
    public void InferredProjectReturnsDirectoryBaseName()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/index.ts"] = "export const x = 1;",
        };
        var (session, _) = SetupWithOptions(files, new SessionOptions
        {
            CurrentDirectory = "/home/projects",
            DefaultLibraryPath = TypeScript.Bundled.Bundled.LibPath(),
            PositionEncoding = PositionEncodingKind.UTF8,
            WatchEnabled = true,
            LoggingEnabled = true,
            PushDiagnosticsEnabled = true,
        });
        session.DidOpenFile(Context.Background, "file:///home/projects/index.ts", 1, "export const x = 1;", LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///home/projects/index.ts");

        var snapshot = session.Snapshot();
        var inferred = snapshot.ProjectCollection.InferredProject();
        Assert.NotNull(inferred);
        var name = inferred.DisplayName("/home");
        Assert.Equal("projects", name);
    }
}

/// <summary>Go: <c>TestProgressNotifications</c>（project_test.go:857-957）。</summary>
public class ProjectProgressNotificationsTests
{
    /// <summary>Go: <c>TestProgressNotifications/emits progress for configured project loading</c>。</summary>
    [Fact]
    public void EmitsProgressForConfiguredProjectLoading()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/tsconfig.json"] = "{}",
            ["/home/projects/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///home/projects/index.ts", 1, "export const x = 1;", LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///home/projects/index.ts");

        var startCalls = utils.Client().ProgressStartCalls();
        var finishCalls = utils.Client().ProgressFinishCalls();

        Assert.True(startCalls.Length > 0, "expected at least one ProgressStart call");
        Assert.True(finishCalls.Length > 0, "expected at least one ProgressFinish call");

        // Go: call.Message == diagnostics.Project_0（*Message 的指针比较）
        Assert.True(
            startCalls.Any(call => ReferenceEquals(call.Message, Project_0)),
            "expected ProgressStart with Project_0 message");
        Assert.True(
            finishCalls.Any(call => ReferenceEquals(call.Message, Project_0)),
            "expected ProgressFinish with Project_0 message");
    }

    /// <summary>Go: <c>TestProgressNotifications/emits progress for inferred project loading</c>。</summary>
    [Fact]
    public void EmitsProgressForInferredProjectLoading()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///home/projects/index.ts", 1, "export const x = 1;", LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///home/projects/index.ts");

        var startCalls = utils.Client().ProgressStartCalls();
        var finishCalls = utils.Client().ProgressFinishCalls();

        Assert.True(startCalls.Length > 0, "expected at least one ProgressStart call");
        Assert.True(finishCalls.Length > 0, "expected at least one ProgressFinish call");
        Assert.True(
            startCalls.Any(call => ReferenceEquals(call.Message, Project_0)),
            "expected ProgressStart with Project_0 message");
    }

    /// <summary>Go: <c>TestProgressNotifications/each start has a matching finish</c>。</summary>
    [Fact]
    public void EachStartHasAMatchingFinish()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/tsconfig.json"] = "{}",
            ["/home/projects/a.ts"] = "export const a = 1;",
            ["/home/projects/b.ts"] = "export const b = 2;",
        };
        var (session, utils) = Setup(files);
        session.DidOpenFile(Context.Background, "file:///home/projects/a.ts", 1, "export const a = 1;", LanguageKind.TypeScript);
        ProjectTestSupport.Open(session, Context.Background, "file:///home/projects/a.ts");

        var startCalls = utils.Client().ProgressStartCalls();
        var finishCalls = utils.Client().ProgressFinishCalls();

        var starts = startCalls.Count(call => ReferenceEquals(call.Message, Project_0));
        var finishes = finishCalls.Count(call => ReferenceEquals(call.Message, Project_0));
        Assert.Equal(starts, finishes);
    }
}
