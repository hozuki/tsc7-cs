// ported from: internal/api/session_completion_test.go:1-256
//
// 与 Go 测试的差异（逐条）：
// 1) `bundled.Embedded` 的跳过分支在本仓不可达（lib 文件已内嵌，`Bundled.LibPath()` 直接可用），不移植；
//    `t.Parallel()` 交给 xunit 调度。
// 2) `projecttestutil.Setup(files)` → `Harness.Setup(files)`（别名取 `Harness` 的理由见 SessionCreateProgramTests.cs 文件头第 1 条）；
//    `NewLSPSession(projectSession, nil)` →
//    `ApiSession.NewLSPSession(projectSession, null)`。
// 3) Go 的 `t.Context()`（Go 1.24 的 testing 上下文）→ `Context.Background`（本仓 `Core.Context`）。
// 4) `session.handleCreateSnapshot` / `handleGetDefaultProjectForFile` / `handleGetCompletionsAtPosition` /
//    `handleGetTypeOfSymbol` → C# 同名 PascalCase 的 `internal` 方法（靠 `InternalsVisibleTo("TypeScript.Compiler.Tests")`）。
// 5) `handleGetCompletionsAtPosition` 在 C# 的签名是 `object?`（与 Go 的 `any` 一致：二进制响应时返回 `RawBinary`）⇒
//    `(CompletionInfoResponse?)…` 强转；默认 `UseBinaryResponses == false`，实际就是 `CompletionInfoResponse`。
//    `handleGetTypeOfSymbol` 返回 `TypeResponse?`（签名与 Go 一致）。
// 6) `uint32(len(content))` → `(uint)content.Length`：content 是纯 ASCII，UTF-16 码元数 == 字节数（Go 注释已说明）。
// 7) `assert.Assert(t, x != nil, "…")` → `Assert.NotNull(x)`（消息作为失败文本保留在注释里）。
// 8) `lsutil.UserPreferences{…}` → `new UserPreferences { … }`（`TypeScript.LsUtil.UserPreferences`）；
//    `core.TSTrue` → `Tristate.TSTrue`。
// 9) 最后一个用例用 goroutine + `time.After(10s)` 表达"不允许死锁"：C# 用 `Thread` + `ManualResetEventSlim.Wait(TimeSpan)`
//    （不用 `Task.Wait`/`.Result`：xunit 分析器 xUnit1031 禁止阻塞式 Task 操作，先例 Project/CheckerPoolTests.cs 文件头第 7 条）。
//    Go 的 `t.Fatal(...)` → `Assert.Fail(...)`。
// 11) 最后一个用例的**超时路径**额外做了两件事（Go 不需要）：`Wait` 超时时不 `Join` 线程、不 `Close` 会话。
//     原因：C# 侧这次会真的死锁（同一个 checker 上再等一次），`Join` 会永久阻塞、`Close` 会再进同一把锁，
//     两者都会把 testhost 拖住（表现为"测试进程挂死/被杀"，而不是一条干净的失败）。线程本身是后台线程。
// 12) Go 的 `defer projectSession.Close()`（`Setup` 起的项目会话）不移植：本仓测试的既有约定是只关 API 会话
//     （先例 Project/ConfigFileChangesTests.cs），对可观察行为无影响（见 SessionCreateProgramTests.cs 文件头第 13 条）。
using System;
using System.Collections.Generic;
using System.Threading;
using TypeScript.Api;
using TypeScript.Core;
using TypeScript.LsUtil;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using ApiSession = TypeScript.Api.Session;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;

namespace TypeScript.Tests;

public class SessionCompletionTests
{
    /// <summary>Go: <c>TestCompletionSymbolTypeIsResolvable</c>（session_completion_test.go:26）。</summary>
    [Fact]
    public void CompletionSymbolTypeIsResolvable()
    {
        const string fileName = "/home/projects/p/src/index.ts";
        // The caret sits right after `people.`, requesting members of `string[]`.
        const string content = "declare const people: string[];\npeople.";
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/home/projects/p/tsconfig.json"] = """{ "compilerOptions": { "strict": true } }""",
            [fileName] = content,
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var snapshotResp = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                OpenFiles = [new DocumentIdentifier { FileName = fileName }],
            });

            var proj = session.HandleGetDefaultProjectForFile(Context.Background, new GetDefaultProjectForFileParams
            {
                Snapshot = snapshotResp.Snapshot,
                File = new DocumentIdentifier { FileName = fileName },
            });
            Assert.NotNull(proj); // "file should resolve to a default project"

            // content is pure ASCII, so the UTF-16 caret offset equals the byte length.
            var completions = (CompletionInfoResponse?)session.HandleGetCompletionsAtPosition(Context.Background, new GetCompletionsAtPositionParams
            {
                Snapshot = snapshotResp.Snapshot,
                Project = proj!.Id,
                File = new DocumentIdentifier { FileName = fileName },
                Position = (uint)content.Length,
                IncludeSymbol = true,
            });
            Assert.NotNull(completions); // "expected a completion list for array members"

            // Resolving the type of every completion symbol must not panic, and known
            // members like `push` must produce a concrete type.
            var sawSymbol = false;
            var sawPush = false;
            foreach (var entry in completions!.Entries)
            {
                if (entry.Symbol is null)
                {
                    continue;
                }

                sawSymbol = true;
                var typeResp = session.HandleGetTypeOfSymbol(Context.Background, new GetTypeOfSymbolParams
                {
                    Snapshot = snapshotResp.Snapshot,
                    Project = proj.Id,
                    Symbol = entry.Symbol.Id,
                });
                Assert.NotNull(typeResp); // "type of completion symbol … should resolve"
                if (entry.Name == "push")
                {
                    sawPush = true;
                }
            }

            Assert.True(sawSymbol); // "completion entries should include resolvable symbols"
            Assert.True(sawPush); // "array member completions should include `push`"
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCompletionOnInferredProject</c>（session_completion_test.go:99）。</summary>
    [Fact]
    public void CompletionOnInferredProject()
    {
        // No tsconfig.json anywhere, so this file belongs to an inferred project.
        const string fileName = "/home/projects/p/src/index.ts";
        const string content = "declare const people: string[];\npeople.";
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            [fileName] = content,
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var snapshotResp = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                OpenFiles = [new DocumentIdentifier { FileName = fileName }],
            });

            var proj = session.HandleGetDefaultProjectForFile(Context.Background, new GetDefaultProjectForFileParams
            {
                Snapshot = snapshotResp.Snapshot,
                File = new DocumentIdentifier { FileName = fileName },
            });
            Assert.NotNull(proj); // "file should resolve to an inferred default project"

            // This request previously panicked in setupLanguageService.
            // content is pure ASCII, so the UTF-16 caret offset equals the byte length.
            var completions = (CompletionInfoResponse?)session.HandleGetCompletionsAtPosition(Context.Background, new GetCompletionsAtPositionParams
            {
                Snapshot = snapshotResp.Snapshot,
                Project = proj!.Id,
                File = new DocumentIdentifier { FileName = fileName },
                Position = (uint)content.Length,
            });
            Assert.NotNull(completions); // "expected a completion list for array members"
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCompletionRetriesWithAutoImports</c>（session_completion_test.go:141）。</summary>
    [Fact]
    public void CompletionRetriesWithAutoImports()
    {
        const string fileName = "/home/projects/p/src/index.ts";
        const string content = "someV";
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/home/projects/p/tsconfig.json"] = """{ "compilerOptions": { "module": "esnext", "target": "esnext" } }""",
            ["/home/projects/p/src/export.ts"] = "export const someValue = 1;",
            [fileName] = content,
        });
        projectSession.Configure(new UserPreferences
        {
            IncludeCompletionsForModuleExports = Tristate.TSTrue,
            IncludeCompletionsForImportStatements = Tristate.TSTrue,
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var snapshotResp = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                OpenFiles = [new DocumentIdentifier { FileName = fileName }],
            });
            var proj = session.HandleGetDefaultProjectForFile(Context.Background, new GetDefaultProjectForFileParams
            {
                Snapshot = snapshotResp.Snapshot,
                File = new DocumentIdentifier { FileName = fileName },
            });
            Assert.NotNull(proj); // "file should resolve to a default project"

            var completions = (CompletionInfoResponse?)session.HandleGetCompletionsAtPosition(Context.Background, new GetCompletionsAtPositionParams
            {
                Snapshot = snapshotResp.Snapshot,
                Project = proj!.Id,
                File = new DocumentIdentifier { FileName = fileName },
                Position = (uint)content.Length,
            });
            Assert.NotNull(completions); // "expected a completion list"
            foreach (var entry in completions!.Entries)
            {
                if (entry.Name == "someValue")
                {
                    return;
                }
            }

            Assert.Fail("expected auto-import completion for someValue");
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCompletionWithSymbolsAndExistingImportDoesNotDeadlock</c>（session_completion_test.go:190）。</summary>
    [Fact]
    public void CompletionWithSymbolsAndExistingImportDoesNotDeadlock()
    {
        const string fileName = "/home/projects/p/src/index.ts";
        const string content = "import { otherValue } from \"./export\";\nsomeV";
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/home/projects/p/tsconfig.json"] = """{ "compilerOptions": { "module": "esnext", "target": "esnext" } }""",
            ["/home/projects/p/src/export.ts"] = "export const otherValue = 0; export const someValue = 1;",
            [fileName] = content,
        });
        projectSession.Configure(new UserPreferences
        {
            IncludeCompletionsForModuleExports = Tristate.TSTrue,
            IncludeCompletionsForImportStatements = Tristate.TSTrue,
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        // 死锁路径的标志位（见 finally）：Wait 超时时既不 Join 也不 Close。
        var hung = false;
        try
        {
            var snapshotResp = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                OpenFiles = [new DocumentIdentifier { FileName = fileName }],
            });
            var proj = session.HandleGetDefaultProjectForFile(Context.Background, new GetDefaultProjectForFileParams
            {
                Snapshot = snapshotResp.Snapshot,
                File = new DocumentIdentifier { FileName = fileName },
            });
            Assert.NotNull(proj); // "file should resolve to a default project"

            // IncludeSymbol pins completion to the single persistent API checker. When
            // ranking the auto-import completion, the existing import makes the view
            // consult that checker. This used to try to acquire the same checker again
            // and deadlock.
            CompletionInfoResponse? completions = null;
            Exception? completionError = null;
            using var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    completions = (CompletionInfoResponse?)session.HandleGetCompletionsAtPosition(Context.Background, new GetCompletionsAtPositionParams
                    {
                        Snapshot = snapshotResp.Snapshot,
                        Project = proj!.Id,
                        File = new DocumentIdentifier { FileName = fileName },
                        Position = (uint)content.Length,
                        IncludeSymbol = true,
                    });
                }
                catch (Exception ex)
                {
                    completionError = ex;
                }
                finally
                {
                    done.Set();
                }
            })
            {
                // Go 的 goroutine 不阻止进程退出；C# 的前台线程会（挂死时会拖住 testhost）⇒ 显式后台化。
                IsBackground = true,
            };
            thread.Start();
            hung = !done.Wait(TimeSpan.FromSeconds(10));
            if (hung)
            {
                // 死锁：不 Join（会永久阻塞）、不 Close（会在同一个 checker 上再等一次）——
                // Go 的 t.Fatal 之后由测试进程自己退出，C# 侧必须显式绕开这两步，否则会把 testhost 拖死。
                Assert.Fail("completion request deadlocked while examining an existing import");
            }

            thread.Join();
            Assert.Null(completionError);
            Assert.NotNull(completions); // "expected a completion list"
            foreach (var entry in completions!.Entries)
            {
                if (entry.Name == "someValue")
                {
                    return;
                }
            }

            Assert.Fail("expected auto-import completion for someValue");
        }
        finally
        {
            if (!hung)
            {
                session.Close();
            }
        }
    }
}
