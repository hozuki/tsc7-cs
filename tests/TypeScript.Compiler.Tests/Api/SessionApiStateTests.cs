// ported from: internal/api/session_apistate_test.go:1-494
//
// 与 Go 测试的差异（逐条）：
//
// 1) `session.handleGetCurrentLanguageServerSnapshot(...)` / `session.toAPISnapshotRequest(...)`（Go 包内可见）
//    → C# 的 `internal` 成员 `Session.HandleGetCurrentLanguageServerSnapshot` / `Session.ToAPISnapshotRequest`
//    （契约 §4"测试可访问性"：Go 同包可见 = C# internal + `InternalsVisibleTo`）。
// 2) `session.openProjects` / `session.openFiles`（Go 未导出字段）→ C# 的 `session._openProjects` /
//    `session._openFiles`（`Collections.Set<T>`，`Len()` 对应 Go 的 `Len()`）；`openProjects.openFiles`
//    是 Go 测试里的字段访问（`session.openProjects.Len()`），C# 一一对应。
// 3) `(T, error)` → 正常路径返回 T、错误路径抛异常：`assert.NilError(t, err)` 消失；
//    `assert.ErrorContains(t, err, msg)` → `var e = Assert.Throws<ClientErrorException>(…); Assert.Contains(msg, e.Message)`。
//    Go 断言的是**裸** handler 错误；C# 的 `HandleGetCurrentLanguageServerSnapshot` 会把 `APIUpdate` 阶段的异常
//    包一层 `ClientErrorException("failed to update language server snapshot: " + inner)`（Go 同样包 `%w`），
//    子串断言因此仍然成立。
// 4) `assert.Assert(t, x != nil)` → `Assert.NotNull(x)`；`== nil` → `Assert.Null(x)`。
//    `assert.Equal(t, projectSession.Snapshot(), baseSnapshot)`：Go 的 `*project.Snapshot` 是指针，gotest.tools 做的是
//    深比较；C# 的 `Project.Snapshot` 没有值相等语义，而本用例真正要钉的是"失败的那次 open 没有被采纳"
//    ⇒ 落成更强的 `Assert.Same`（同一个实例 = 没有被替换）。`Project.Session.Snapshot()` 返回的就是持有的那个实例
//    （Project/Session.cs:1290），所以两者在这里等价。
// 5) `bundled.Embedded` 的 `t.Skip("bundled files are not embedded")` **不移植**：C# 的 `Bundled.Embedded`
//    是 `const bool = true`（Bundled/Bundled.cs）⇒ 分支不可达（仓内先例：Project/SnapshotTests.cs 文件头）。
// 6) `tspath.Path(p)`（Go 的纯类型转换）→ C# 的 `new Path(p)`；`core.CompilerOptions{NoLib: core.TSTrue}` →
//    `new Core.CompilerOptions { NoLib = Tristate.TSTrue }`（`Tristate` 是 C# enum，契约 §2）。
// 7) `project.SyntheticProjectID(created.Projects[0].Id)`（Go 两个 string 底层类型之间的转换）→ C# 没有对应
//    运算符 ⇒ `new Project.SyntheticProjectID(created.Projects[0]!.Id.Value)`（只取底层字符串）。
// 8) `DocumentIdentifier{FileName: k}` → `new DocumentIdentifier { FileName = k }`；`.ToURI(cwd)` 同名同参。
//    `lsproto.FileChangeTypeChanged` → `FileChangeType.Changed`（枚举去前缀，契约 §2）。
// 9) `defer session.Close()` → `try/finally`（`Session` 不实现 `IDisposable`；先例：Project/SessionTests.cs）。
//    用例里显式调用 `Close()` 的地方（验证关闭后的状态）照旧保留在 try 内。
// 10) `assert.DeepEqual(t, *reopened.Operation.OpenedFiles, *first.Operation.OpenedFiles)`：C# 的
//     `OpenedFileOperationResult` 是普通类（无值相等）⇒ 逐元素比较 `Project`（Go 的比较实际也只有该字段有值）。
// 11) 测试辅助函数 `configuredProjectID`/`inferredProjectID`/`syntheticProjectID` → C# 私有静态方法
//     `ConfiguredProjectID`/`InferredProjectID`/`SyntheticProjectID`（方法一律 PascalCase，见 .editorconfig）。
// 12) 用例类收进 `[Collection("ProjectHubSerial")]`（禁并行）：这些用例建真实 project 会话、驱动共享静态状态，
//     与 Project/SessionTests.cs 同因；`t.Parallel()` 不移植。
// 13) `projecttestutil.Setup(files)` → `Setup(files)`（`using static …ProjectTestUtil`；
//     `ProjectTestUtil` 既是命名空间又是类，裸写会 CS0234）。`project.NewSession(init)` → `new Project.Session(init)`。
namespace TypeScript.Tests;

using System.Collections.Generic;
using TypeScript.Api;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using static TypeScript.Api.Api;
using static TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;
using ApiSession = TypeScript.Api.Session;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>internal/api/session_apistate_test.go</c> 的 15 个用例。</summary>
[Collection("ProjectHubSerial")]
public class SessionApiStateTests
{
    /// <summary>Go: <c>func configuredProjectID(path string) project.ID</c>（session_apistate_test.go:16-18）。</summary>
    private static Project.ID ConfiguredProjectID(string path) =>
        new Project.ConfiguredProjectID(new Path(path)).AsID();

    /// <summary>Go: <c>func inferredProjectID() project.ID</c>（session_apistate_test.go:20-22）。</summary>
    private static Project.ID InferredProjectID() => new("/dev/null/inferred");

    /// <summary>Go: <c>func syntheticProjectID(id int) project.SyntheticProjectID</c>（session_apistate_test.go:24-26）。</summary>
    private static Project.SyntheticProjectID SyntheticProjectID(int id) =>
        Project.SyntheticProjectID.NewSyntheticProjectID(id);

    /// <summary>Go: <c>TestGetCurrentLanguageServerSnapshotAdoptsChanges</c>（session_apistate_test.go:28-85）。</summary>
    [Fact]
    public void TestGetCurrentLanguageServerSnapshotAdoptsChanges()
    {
        const string configFileName = "/home/projects/p/tsconfig.json";
        const string fileName = "/home/projects/p/src/index.ts";
        var (projectSession, utils) = Setup(new Dictionary<string, object?>
        {
            [configFileName] = """{ "compilerOptions": { "strict": true } }""",
            [fileName] = "export const x = 1;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var ctx = Context.Background;
            var response = session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    OpenProjects = [new DocumentIdentifier { FileName = configFileName }],
                },
            });
            Assert.Equal(1, session._openProjects.Len());
            Assert.Equal(SnapshotHandle(projectSession.Snapshot()), response.Snapshot);
            Assert.NotNull(projectSession.Snapshot().ProjectCollection.ConfiguredProject(new Path(configFileName)));
            utils.FS().WriteFile(fileName, "export const x = 2;");
            projectSession.DidChangeWatchedFiles(ctx,
            [
                new FileEvent
                {
                    Uri = new DocumentIdentifier { FileName = fileName }.ToURI(projectSession.GetCurrentDirectory()),
                    Type = FileChangeType.Changed,
                },
            ]);
            var dirty = session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                BaseSnapshot = response.Snapshot,
            });
            Assert.True(projectSession.Snapshot().ProjectCollection.ConfiguredProject(new Path(configFileName))!.IsDirty());

            var unchanged = session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                BaseSnapshot = dirty.Snapshot,
                Changes = new LanguageServerSnapshotChanges
                {
                    OpenProjects = [new DocumentIdentifier { FileName = configFileName }],
                },
            });
            Assert.False(unchanged.Projects[0]!.Dirty);
            Assert.Equal(1, session._openProjects.Len());

            var removed = session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                BaseSnapshot = unchanged.Snapshot,
                Changes = new LanguageServerSnapshotChanges
                {
                    CloseProjects = [new DocumentIdentifier { FileName = configFileName }],
                },
            });
            Assert.Empty(removed.Projects);
            Assert.Equal(new[] { ConfiguredProjectID(configFileName) }, removed.Changes!.RemovedProjects!);
            Assert.Equal(0, session._openProjects.Len());

            session.Close();
            Assert.Equal(0, session._openProjects.Len());
            Assert.Null(projectSession.Snapshot().ProjectCollection.ConfiguredProject(new Path(configFileName)));
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestGetCurrentLanguageServerSnapshotRejectsStandaloneSession</c>（session_apistate_test.go:87-96）。</summary>
    [Fact]
    public void TestGetCurrentLanguageServerSnapshotRejectsStandaloneSession()
    {
        var (init, _) = GetSessionInitOptions(new Dictionary<string, object?>(), null, new TypingsInstallerOptions());
        var session = ApiSession.NewStandaloneSession(init, null);
        try
        {
            var e = Assert.Throws<ClientErrorException>(() => session.HandleGetCurrentLanguageServerSnapshot(
                Context.Background,
                new GetCurrentLanguageServerSnapshotParams()));
            Assert.Contains("requires an LSP-connected API session", e.Message);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestOpenProjectRejectsReservedProjectID</c>（session_apistate_test.go:98-109）。</summary>
    [Fact]
    public void TestOpenProjectRejectsReservedProjectID()
    {
        var (init, _) = GetSessionInitOptions(new Dictionary<string, object?>(), null, new TypingsInstallerOptions());
        var session = ApiSession.NewStandaloneSession(init, null);
        try
        {
            var e = Assert.Throws<ClientErrorException>(() => session.ToAPISnapshotRequest(
                Context.Background,
                new SnapshotRequestChangesParams
                {
                    OpenProjects = [new DocumentIdentifier { FileName = "/dev/null/inferred" }],
                }));
            Assert.Contains("invalid configured project ID", e.Message);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestGetCurrentLanguageServerSnapshotCloseAndReopenProject</c>（session_apistate_test.go:111-142）。</summary>
    [Fact]
    public void TestGetCurrentLanguageServerSnapshotCloseAndReopenProject()
    {
        const string configFileName = "/home/projects/p/tsconfig.json";
        var (projectSession, _) = Setup(new Dictionary<string, object?> { [configFileName] = "{}" });
        var ctx = Context.Background;
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var open = new DocumentIdentifier { FileName = configFileName };
            session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges { OpenProjects = [open] },
            });

            session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    CloseProjects = [open],
                    OpenProjects = [open],
                },
            });
            Assert.Equal(1, session._openProjects.Len());
            Assert.NotNull(projectSession.Snapshot().ProjectCollection.ConfiguredProject(new Path(configFileName)));

            session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges { CloseProjects = [open] },
            });
            Assert.Equal(0, session._openProjects.Len());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestGetCurrentLanguageServerSnapshotCloseAndReopenFile</c>（session_apistate_test.go:144-175）。</summary>
    [Fact]
    public void TestGetCurrentLanguageServerSnapshotCloseAndReopenFile()
    {
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, _) = Setup(new Dictionary<string, object?>
        {
            ["/home/projects/p/tsconfig.json"] = "{}",
            [fileName] = "export const value = 1;",
        });
        var ctx = Context.Background;
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var open = new DocumentIdentifier { FileName = fileName };
            session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges { OpenFiles = [open] },
            });

            session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    CloseFiles = [open],
                    OpenFiles = [open],
                },
            });
            Assert.Equal(1, session._openFiles.Len());
            Assert.NotNull(projectSession.Snapshot().GetDefaultProject(open.ToURI(projectSession.GetCurrentDirectory())));

            session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges { CloseFiles = [open] },
            });
            Assert.Equal(0, session._openFiles.Len());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestGetCurrentLanguageServerSnapshotFlushesPendingLSPChanges</c>（session_apistate_test.go:177-203）。</summary>
    [Fact]
    public void TestGetCurrentLanguageServerSnapshotFlushesPendingLSPChanges()
    {
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, _) = Setup(new Dictionary<string, object?>
        {
            [fileName] = "export const value: string = 1;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            projectSession.DidOpenFile(
                Context.Background,
                new DocumentIdentifier { FileName = fileName }.ToURI(projectSession.GetCurrentDirectory()),
                1,
                """export const value: string = "ok";""",
                LanguageKind.TypeScript);

            var response = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams());
            Assert.Equal(SnapshotHandle(projectSession.Snapshot()), response.Snapshot);

            var snapshot = session.GetSnapshotData(response.Snapshot);
            Assert.Equal("""export const value: string = "ok";""", snapshot.Snapshot.GetFile(fileName)!.Content());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestGetCurrentLanguageServerSnapshotReportsOpenedFilesInRequestOrder</c>（session_apistate_test.go:205-244）。</summary>
    [Fact]
    public void TestGetCurrentLanguageServerSnapshotReportsOpenedFilesInRequestOrder()
    {
        const string configuredFile = "/home/projects/p/index.ts";
        const string inferredFile = "/home/projects/loose.ts";
        var (projectSession, utils) = Setup(new Dictionary<string, object?>
        {
            ["/home/projects/p/tsconfig.json"] = "{}",
            [configuredFile] = "export const configured = 1;",
            [inferredFile] = "export const inferred = 1;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var changes = new LanguageServerSnapshotChanges
            {
                OpenFiles = [new DocumentIdentifier { FileName = inferredFile }, new DocumentIdentifier { FileName = configuredFile }],
            };
            var first = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = changes,
            });
            Assert.Equal(2, first.Operation!.OpenedFiles!.Length);
            Assert.Equal(InferredProjectID(), first.Operation.OpenedFiles[0]!.Project);
            Assert.Equal(ConfiguredProjectID("/home/projects/p/tsconfig.json"), first.Operation.OpenedFiles[1]!.Project);
            utils.FS().WriteFile(configuredFile, "export const configured = 2;");
            projectSession.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent
                {
                    Uri = new DocumentIdentifier { FileName = configuredFile }.ToURI(projectSession.GetCurrentDirectory()),
                    Type = FileChangeType.Changed,
                },
            ]);
            var dirty = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                BaseSnapshot = first.Snapshot,
            });
            Assert.True(projectSession.Snapshot().ProjectCollection
                .ConfiguredProject(new Path("/home/projects/p/tsconfig.json"))!.IsDirty());

            var reopened = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                BaseSnapshot = dirty.Snapshot,
                Changes = changes,
            });
            Assert.Equal(first.Operation.OpenedFiles.Length, reopened.Operation!.OpenedFiles!.Length);
            for (var i = 0; i < first.Operation.OpenedFiles.Length; i++)
            {
                Assert.Equal(first.Operation.OpenedFiles[i]!.Project, reopened.Operation.OpenedFiles[i]!.Project);
            }

            Assert.False(projectSession.Snapshot().ProjectCollection
                .ConfiguredProject(new Path("/home/projects/p/tsconfig.json"))!.IsDirty());
            Assert.Equal(2, session._openFiles.Len());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestGetCurrentLanguageServerSnapshotCreatesAndRemovesPrograms</c>（session_apistate_test.go:246-275）。</summary>
    [Fact]
    public void TestGetCurrentLanguageServerSnapshotCreatesAndRemovesPrograms()
    {
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, _) = Setup(new Dictionary<string, object?> { [fileName] = "export const value = 1;" });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var created = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    CreatePrograms =
                    [
                        new CreateSnapshotProgramParams
                        {
                            RootFiles = [new DocumentIdentifier { FileName = fileName }],
                            CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                        },
                    ],
                },
            });
            Assert.Single(created.Projects);
            Assert.Single(projectSession.Snapshot().ProjectCollection.SyntheticProjects());

            var removed = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    RemovePrograms = [SyntheticProjectID(1), SyntheticProjectID(1)],
                },
            });
            Assert.Empty(removed.Projects);
            Assert.Empty(projectSession.Snapshot().ProjectCollection.SyntheticProjects());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestOpenFilePreservesWindowsDriveLetterCase</c>（session_apistate_test.go:277-305）。</summary>
    [Fact]
    public void TestOpenFilePreservesWindowsDriveLetterCase()
    {
        const string fileName = "D:/repo/index.ts";
        var (init, _) = GetSessionInitOptions(new Dictionary<string, object?>
        {
            ["D:/repo/tsconfig.json"] = "{}",
            [fileName] = "export const value = 1;",
        }, null, new TypingsInstallerOptions());
        init.Options.CurrentDirectory = "D:/repo";
        var projectSession = new Project.Session(init);
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    OpenFiles = [new DocumentIdentifier { FileName = fileName }],
                },
            });

            var project = response.Projects[0]!;
            var snapshot = session.GetSnapshotData(response.Snapshot);
            var program = snapshot.GetProgram(project.Id);
            Assert.Equal(fileName, program.GetSourceFile(fileName)!.FileName());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestClosingAPISessionRemovesCreatedLanguageServerPrograms</c>（session_apistate_test.go:307-328）。</summary>
    [Fact]
    public void TestClosingAPISessionRemovesCreatedLanguageServerPrograms()
    {
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, _) = Setup(new Dictionary<string, object?> { [fileName] = "export const value = 1;" });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    CreatePrograms =
                    [
                        new CreateSnapshotProgramParams
                        {
                            RootFiles = [new DocumentIdentifier { FileName = fileName }],
                            CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                        },
                    ],
                },
            });
            Assert.Single(projectSession.Snapshot().ProjectCollection.SyntheticProjects());

            session.Close();
            Assert.Empty(projectSession.Snapshot().ProjectCollection.SyntheticProjects());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestLanguageServerProgramOwnershipIsIsolatedByAPISession</c>（session_apistate_test.go:330-361）。</summary>
    [Fact]
    public void TestLanguageServerProgramOwnershipIsIsolatedByAPISession()
    {
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, _) = Setup(new Dictionary<string, object?> { [fileName] = "export const value = 1;" });
        var owner = ApiSession.NewLSPSession(projectSession, null);
        var other = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            owner.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    CreatePrograms =
                    [
                        new CreateSnapshotProgramParams
                        {
                            RootFiles = [new DocumentIdentifier { FileName = fileName }],
                            CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                        },
                    ],
                },
            });

            other.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges { RemovePrograms = [SyntheticProjectID(1)] },
            });
            Assert.Single(projectSession.Snapshot().ProjectCollection.SyntheticProjects());

            other.Close();
            Assert.Single(projectSession.Snapshot().ProjectCollection.SyntheticProjects());
            owner.Close();
            Assert.Empty(projectSession.Snapshot().ProjectCollection.SyntheticProjects());
        }
        finally
        {
            other.Close();
            owner.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestLanguageServerProgramReconfigurationIsIsolatedByAPISession</c>（session_apistate_test.go:363-395）。</summary>
    [Fact]
    public void TestLanguageServerProgramReconfigurationIsIsolatedByAPISession()
    {
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, _) = Setup(new Dictionary<string, object?> { [fileName] = "export const value = 1;" });
        var owner = ApiSession.NewLSPSession(projectSession, null);
        var other = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var created = owner.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    CreatePrograms =
                    [
                        new CreateSnapshotProgramParams
                        {
                            RootFiles = [new DocumentIdentifier { FileName = fileName }],
                            CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                        },
                    ],
                },
            });
            var programID = new Project.SyntheticProjectID(created.Projects[0]!.Id.Value);

            var e = Assert.Throws<ClientErrorException>(() => other.HandleGetCurrentLanguageServerSnapshot(
                Context.Background,
                new GetCurrentLanguageServerSnapshotParams
                {
                    Changes = new LanguageServerSnapshotChanges
                    {
                        ReconfigurePrograms =
                        [
                            new ReconfigureSnapshotProgramParams
                            {
                                Id = programID,
                                RootFiles = [new DocumentIdentifier { FileName = fileName }],
                                CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue, Strict = Tristate.TSTrue },
                            },
                        ],
                    },
                }));
            Assert.Contains("not owned by this API session", e.Message);
        }
        finally
        {
            other.Close();
            owner.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestOpeningProjectOwnedByAnotherAPISessionEnsuresProgram</c>（session_apistate_test.go:397-433）。</summary>
    [Fact]
    public void TestOpeningProjectOwnedByAnotherAPISessionEnsuresProgram()
    {
        const string configFileName = "/home/projects/p/tsconfig.json";
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, utils) = Setup(new Dictionary<string, object?>
        {
            [configFileName] = "{}",
            [fileName] = "export const value = 1;",
        });
        var openProject = new LanguageServerSnapshotChanges
        {
            OpenProjects = [new DocumentIdentifier { FileName = configFileName }],
        };
        var owner = ApiSession.NewLSPSession(projectSession, null);
        var other = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            owner.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams { Changes = openProject });
            utils.FS().WriteFile(fileName, "export const value = 2;");
            projectSession.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent
                {
                    Uri = new DocumentIdentifier { FileName = fileName }.ToURI(projectSession.GetCurrentDirectory()),
                    Type = FileChangeType.Changed,
                },
            ]);
            owner.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams());
            Assert.True(projectSession.Snapshot().ProjectCollection.ConfiguredProject(new Path(configFileName))!.IsDirty());

            var opened = other.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams { Changes = openProject });
            Assert.False(opened.Projects[0]!.Dirty);
            Assert.Equal(1, other._openProjects.Len());

            other.Close();
            Assert.NotNull(projectSession.Snapshot().ProjectCollection.ConfiguredProject(new Path(configFileName)));
            owner.Close();
            Assert.Null(projectSession.Snapshot().ProjectCollection.ConfiguredProject(new Path(configFileName)));
        }
        finally
        {
            other.Close();
            owner.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestFailedLanguageServerSnapshotOpenIsNotAdopted</c>（session_apistate_test.go:435-456）。</summary>
    [Fact]
    public void TestFailedLanguageServerSnapshotOpenIsNotAdopted()
    {
        var (projectSession, _) = Setup(new Dictionary<string, object?> { ["/notes.txt"] = "text" });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseSnapshot = projectSession.Snapshot();
            var ctx = Context.Background;
            var e = Assert.Throws<ClientErrorException>(() => session.HandleGetCurrentLanguageServerSnapshot(
                ctx,
                new GetCurrentLanguageServerSnapshotParams
                {
                    Changes = new LanguageServerSnapshotChanges
                    {
                        OpenFiles = [new DocumentIdentifier { FileName = "/notes.txt" }],
                    },
                }));

            Assert.Contains("no project found for opened file", e.Message);
            Assert.Same(baseSnapshot, projectSession.Snapshot());
            Assert.Equal(0, session._openFiles.Len());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestGetCurrentLanguageServerSnapshotOpeningLSPFileEnsuresConfiguredProgram</c>（session_apistate_test.go:458-493）。</summary>
    [Fact]
    public void TestGetCurrentLanguageServerSnapshotOpeningLSPFileEnsuresConfiguredProgram()
    {
        const string configFileName = "/home/projects/p/tsconfig.json";
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, _) = Setup(new Dictionary<string, object?>
        {
            [configFileName] = "{}",
            [fileName] = "export const value = 1;",
        });
        var uri = new DocumentIdentifier { FileName = fileName }.ToURI(projectSession.GetCurrentDirectory());
        projectSession.DidOpenFile(Context.Background, uri, 1, "export const value = 1;", LanguageKind.TypeScript);
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var initial = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams());
            Assert.False(initial.Projects[0]!.Dirty);
            var projectID = initial.Projects[0]!.Id;

            projectSession.DidChangeFile(Context.Background, uri, 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    WholeDocument = new TextDocumentContentChangeWholeDocument { Text = "export const value = 2;" },
                },
            ]);
            var dirty = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                BaseSnapshot = initial.Snapshot,
            });
            Assert.True(dirty.Projects[0]!.Dirty);

            var ensured = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                BaseSnapshot = dirty.Snapshot,
                Changes = new LanguageServerSnapshotChanges
                {
                    OpenFiles = [new DocumentIdentifier { FileName = fileName }],
                },
            });
            Assert.False(ensured.Projects[0]!.Dirty);
            Assert.Equal(projectID, ensured.Operation!.OpenedFiles![0]!.Project);
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }
}
