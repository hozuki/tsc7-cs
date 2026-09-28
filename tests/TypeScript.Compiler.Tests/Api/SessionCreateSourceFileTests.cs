// ported from: internal/api/session_createsourcefile_test.go:1-167
//
// 与 Go 测试的差异（逐条）：
// 1) Go 的一个 `TestCreateSourceFile` 带 8 个 `t.Run` 子测试；xunit 没有子测试嵌套 ⇒ 落成同一个 `[Fact]` 内按
//    原顺序的 8 段（每段对应一个 `t.Run`，名称逐字保留在注释里）。Go 的 `t.Cleanup(lease.Release)` →
//    `try/finally`（csharp 的 cleanup 是 LIFO，这里每个 lease 各自 finally）。
// 2) `projecttestutil.Setup(files)` → `Harness.Setup(files)`（别名取 `Harness` 的理由见 SessionCreateProgramTests.cs 文件头第 1 条）；
//    `NewLSPSession(projectSession, nil)` →
//    `ApiSession.NewLSPSession(projectSession, null)`。
// 3) `(T, error)` → 返回值 + 抛异常；`assert.NilError` 不需要，`assert.ErrorContains` → `Assert.Throws<ClientErrorException>` + `Assert.Contains`。
// 4) `core.ScriptKindTSX` → `ScriptKind.TSX`（`TypeScript.Core.ScriptKind`）；`CreateSourceFileOptions{}` →
//    `new CreateSourceFileOptions()`；Go 的 `CreateSourceFileOptions{ScriptKind: 999}`（无类型的整数常量）→
//    `(ScriptKind)999`（C# 强转是显式必需的）。
// 5) `string(sourceFile.Path())` → `sourceFile.Path().Value`（`TSPath.Path` 是包 string 的 record struct）。
// 6) `len(sourceFile.Statements.Nodes)` → `sourceFile.Statements!.Nodes!.Length`（C# 的字段是 `NodeList?` / `Node[]?`）。
// 7) `assert.Assert(t, programFile == direct.SourceFile())`（指针同一）→ `Assert.Same(programFile, direct.SourceFile())`。
//    `session.acquireSourceFile(...)` → `session.AcquireSourceFile(...)`。
// 8) `session.sourceFileLeases` / `session.sourceFileLeasesMu` → `session._sourceFileLeases`（`Dictionary<SourceFileLeaseID,
//    SourceFileLease>`）/ `session._sourceFileLeasesMu`（`System.Threading.Lock` ⇒ `lock (...)`，本仓契约 §2）。
//    Go 的零值 `SourceFileLeaseID(0)` → `default(SourceFileLeaseID)`。
// 9) `session.createSourceFile` / `handleCreateSourceFile` / `handleReleaseSourceFile` / `handleCreateSourceFileFromFile`
//    在 C# 是 `CreateSourceFile` / `HandleCreateSourceFile` / `HandleReleaseSourceFile` / `HandleCreateSourceFileFromFile`
//    （`internal`，测试程序集靠 `InternalsVisibleTo` 访问）。
// 10) `lsproto.LanguageKindTypeScript` → `LanguageKind.TypeScript`（`TypeScript.LsProto.LanguageKind`）。
// 11) `cacheProjectSession.GetLanguageService(ctx, uri)` 保留双返回 `(LanguageService?, Exception?)` ⇒
//     `Assert.Null(error)` 对应 Go 的 `assert.NilError(t, err)`（用裸调用而非 `ProjectTestSupport.Open`，以保住这条断言）。
// 12) Go 的 `defer projectSession.Close()`（`Setup` 起的项目会话）不移植：本仓测试的既有约定是只关 API 会话
//     （先例 Project/ConfigFileChangesTests.cs），对可观察行为无影响（见 SessionCreateProgramTests.cs 文件头第 13 条）。
//     Go 子测试 "shares parse cache with programs" 里的 `cacheProjectSession` 是**同一测试**内部的第二个项目会话，
//     该分支按原样关掉了它（`cacheSession.Close(); cacheProjectSession.Close();`）。
using System.Collections.Generic;
using TypeScript.Api;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using ApiSession = TypeScript.Api.Session;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;

namespace TypeScript.Tests;

public class SessionCreateSourceFileTests
{
    /// <summary>Go: <c>TestCreateSourceFile</c>（session_createsourcefile_test.go:13）。</summary>
    [Fact]
    public void CreateSourceFile()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/src/input.ts"] = "export const fromFile = 1;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            TextSubtest(session);
            ScriptKindOverrideSubtest(session);
            SharesParseCacheWithProgramsSubtest(session);
            LeaseReleaseSubtest(session);
            UnknownExtensionDefaultsToTypeScriptSubtest(session);
            FromFileSubtest(session);
            InvalidScriptKindSubtest(session);
            MissingFileSubtest(session);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCreateSourceFile/text</c>（session_createsourcefile_test.go:24）。</summary>
    private static void TextSubtest(ApiSession session)
    {
        var lease = session.CreateSourceFile("src/input.tsx", "export const element = <div />;", new CreateSourceFileOptions());
        try
        {
            var sourceFile = lease.SourceFile();
            Assert.Equal("/src/input.tsx", sourceFile.FileName());
            Assert.Equal("/src/input.tsx", sourceFile.Path().Value);
            Assert.Equal("export const element = <div />;", sourceFile.Text());
            Assert.Equal(ScriptKind.TSX, sourceFile.ScriptKind);
            Assert.Single(sourceFile.Statements!.Nodes!);
            Assert.True(sourceFile.IsBound());
        }
        finally
        {
            lease.Release();
        }
    }

    /// <summary>Go: <c>TestCreateSourceFile/script kind override</c>（session_createsourcefile_test.go:43）。</summary>
    private static void ScriptKindOverrideSubtest(ApiSession session)
    {
        var lease = session.CreateSourceFile(
            "/src/component.txt",
            "export const element = <div />;",
            new CreateSourceFileOptions { ScriptKind = ScriptKind.TSX });
        try
        {
            var sourceFile = lease.SourceFile();
            Assert.Equal(ScriptKind.TSX, sourceFile.ScriptKind);
            Assert.Empty(sourceFile.Diagnostics());
        }
        finally
        {
            lease.Release();
        }
    }

    /// <summary>Go: <c>TestCreateSourceFile/shares parse cache with programs</c>（session_createsourcefile_test.go:58）。</summary>
    private static void SharesParseCacheWithProgramsSubtest(ApiSession session)
    {
        const string fileName = "/src/shared.ts";
        const string sourceText = "export const shared = 1;";
        var (cacheProjectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            [fileName] = sourceText,
        });
        var cacheSession = ApiSession.NewLSPSession(cacheProjectSession, null);
        try
        {
            cacheProjectSession.DidOpenFile(Context.Background, "file:///src/shared.ts", 1, sourceText, LanguageKind.TypeScript);
            var (languageService, error) = cacheProjectSession.GetLanguageService(Context.Background, "file:///src/shared.ts");
            Assert.Null(error);
            cacheProjectSession.WaitForBackgroundTasks();

            var programFile = languageService!.GetProgram()!.GetSourceFile(fileName)!;
            var direct = cacheSession.AcquireSourceFile(programFile.ParseOptions(), sourceText, programFile.ScriptKind);
            try
            {
                Assert.Same(programFile, direct.SourceFile());
            }
            finally
            {
                direct.Release();
            }
        }
        finally
        {
            cacheSession.Close();
            cacheProjectSession.Close();
        }
    }

    /// <summary>Go: <c>TestCreateSourceFile/lease release</c>（session_createsourcefile_test.go:79）。</summary>
    private static void LeaseReleaseSubtest(ApiSession session)
    {
        SourceFileLeaseID FindLease(string fileName)
        {
            lock (session._sourceFileLeasesMu)
            {
                foreach (var (id, lease) in session._sourceFileLeases)
                {
                    if (lease.SourceFile().FileName() == fileName)
                    {
                        return id;
                    }
                }
            }

            return default;
        }

        var first = session.HandleCreateSourceFile(Context.Background, new CreateSourceFileParams
        {
            FileName = "/src/lease-1.ts",
            SourceText = "export {};",
        });
        Assert.NotNull(first);
        var firstLease = FindLease("/src/lease-1.ts");
        Assert.NotEqual(default(SourceFileLeaseID), firstLease);

        var second = session.HandleCreateSourceFile(Context.Background, new CreateSourceFileParams
        {
            FileName = "/src/lease-2.ts",
            SourceText = "export {};",
        });
        Assert.NotNull(second);
        var secondLease = FindLease("/src/lease-2.ts");
        Assert.NotEqual(default(SourceFileLeaseID), secondLease);
        Assert.NotEqual(firstLease, secondLease);

        session.HandleReleaseSourceFile(new ReleaseSourceFileParams { Lease = firstLease });
        Assert.Equal(default(SourceFileLeaseID), FindLease("/src/lease-1.ts"));

        var ex = Assert.Throws<ClientErrorException>(() => session.HandleReleaseSourceFile(new ReleaseSourceFileParams { Lease = firstLease }));
        Assert.Contains("source file lease", ex.Message);

        session.HandleReleaseSourceFile(new ReleaseSourceFileParams { Lease = secondLease });
    }

    /// <summary>Go: <c>TestCreateSourceFile/unknown extension defaults to TypeScript</c>（session_createsourcefile_test.go:123）。</summary>
    private static void UnknownExtensionDefaultsToTypeScriptSubtest(ApiSession session)
    {
        var lease = session.CreateSourceFile(
            "/src/component.txt",
            "export const value: string = \"ok\";",
            new CreateSourceFileOptions());
        try
        {
            var sourceFile = lease.SourceFile();
            Assert.Equal(ScriptKind.TS, sourceFile.ScriptKind);
            Assert.Empty(sourceFile.Diagnostics());
        }
        finally
        {
            lease.Release();
        }
    }

    /// <summary>Go: <c>TestCreateSourceFile/from file</c>（session_createsourcefile_test.go:148）。</summary>
    private static void FromFileSubtest(ApiSession session)
    {
        var result = session.HandleCreateSourceFileFromFile(Context.Background, new CreateSourceFileFromFileParams
        {
            FileName = "/src/input.ts",
        });
        Assert.NotNull(result);
    }

    /// <summary>Go: <c>TestCreateSourceFile/invalid script kind</c>（session_createsourcefile_test.go:148）。</summary>
    private static void InvalidScriptKindSubtest(ApiSession session)
    {
        var ex = Assert.Throws<ClientErrorException>(() => session.CreateSourceFile(
            "/src/input.ts",
            "",
            new CreateSourceFileOptions { ScriptKind = (ScriptKind)999 }));
        Assert.Contains("invalid scriptKind 999", ex.Message);
    }

    /// <summary>Go: <c>TestCreateSourceFile/missing file</c>（session_createsourcefile_test.go:159）。</summary>
    private static void MissingFileSubtest(ApiSession session)
    {
        var ex = Assert.Throws<ClientErrorException>(() => session.HandleCreateSourceFileFromFile(Context.Background, new CreateSourceFileFromFileParams
        {
            FileName = "/src/missing.ts",
        }));
        Assert.Contains("could not read file \"/src/missing.ts\"", ex.Message);
    }
}
