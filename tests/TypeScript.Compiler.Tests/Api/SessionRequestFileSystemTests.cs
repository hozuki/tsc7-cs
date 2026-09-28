// ported from: internal/api/session_requestfilesystem_test.go:1-1029
//
// 与 Go 测试的差异（逐条）：
// 1) 顶层 helper `updateCurrentLanguageServerSnapshot(ctx, session, changes)`（第 16 行）逐字移植为
//    `UpdateCurrentLanguageServerSnapshot(ctx, session, changes)`。Go 靠**内嵌结构体赋值**把
//    `changes.SnapshotRequestChangesParams` 塞进 `LanguageServerSnapshotChanges` / `CreateSnapshotParams`；
//    C# 的 Proto 切片把内嵌扁平成了**基类**（`SnapshotRequestChangesParams`），且两个派生类互不兼容，
//    所以这里用 `CopyBaseFields(src, dst)` 逐字段搬（8 个字段，与 Go 的内嵌字段一一对应、无遗漏无新增）；
//    Go 第二段里 `changes.SnapshotRequestChangesParams` 复制后再加 `EnsurePrograms{All:true}` + `FileSystem`
//    的写法，落成"先 CopyBaseFields、再覆盖这两个字段"，语义与"复制值"一致。
// 2) `projecttestutil.Setup(files)` → `Harness.Setup(files)`（别名取 `Harness` 的理由见 SessionCreateProgramTests.cs 文件头第 1 条）；
//    `NewLSPSession(projectSession, nil)` →
//    `ApiSession.NewLSPSession(projectSession, null)`。
// 3) Go 测试直接读 `session.snapshots[h]`（`snapshotData`）与其 `.snapshot`/`.fileSystem`/`.refCount`：
//    C# 是 `session._snapshots[h]`（`Dictionary<SnapshotID, SnapshotData>`）与
//    `.Snapshot`/`.FileSystem`/`.RefCount`（均为 `internal`/`public`，靠 `InternalsVisibleTo`，先例
//    Project/CheckerPoolTests.cs 文件头第 4 条）。`session.snapshots[h] == nil` → `!_snapshots.ContainsKey(h)`。
// 4) `snapshot.ReadFile(path)` 的 `(string, bool)` → `snapshot.TryReadFile(path, out contents)`；
//    `fileSystem.ReadFile(...)`/`fileSystem.FileExists(...)` → `Vfs.FS.TryReadFile`/`FileExists`。
// 5) `requestfilesystem.KindFull`/`KindLayer` → `Kind.Full`/`Kind.Layer`（`TypeScript.Api.RequestFileSystem.Kind`
//    是包 string 的 readonly struct）；`requestfilesystem.HasFullFileSystem(fs)` →
//    `RequestFileSystem.HasFullFileSystem(fs)`（Go 包级函数 → 消息类型上的 static，见该文件头第 11 条）。
// 6) `project.ID("/tsconfig.json")` → `new ProjectNs.ID("/tsconfig.json")`；`project.ProgramUpdateKindCloned` →
//    `ProgramUpdateKind.ProgramUpdateKindCloned`（枚举成员名与 Go 常量同名，`TypeScript.Project` 枚举）。
//    `ProjectCollection.GetOpenConfiguredProjects().Has("/tsconfig.json")` → C# 的 Set 元素是
//    `ConfiguredProjectID`（不是 Path）⇒ `new ConfiguredProjectID(new Path("/tsconfig.json"))`。
// 7) `strconv.Itoa(i)` → `i.ToString(CultureInfo.InvariantCulture)`；`fmt.Sprintf("/pkg/file%d.ts", i)` →
//    字符串插值（插值的 int 走 InvariantCulture 之外的默认格式，这里显式 ToString(InvariantCulture) 保持一致）。
// 8) `assert.DeepEqual(t, a, b)` → `Assert.Equal(expected, actual)`；`assert.Assert(t, a == b)`（快照/Program/文件句柄
//    指针同一）→ `Assert.Same`；`assert.Assert(t, x != nil)` → `Assert.NotNull`。
// 9) `for kind := range []Kind{KindFull, KindLayer} { t.Run(...) }` 与
//    `for baseKind := range []Kind{"host", KindFull, KindLayer}` 的**子测试**落成同一个 `[Fact]` 里的循环
//    （xunit 没有子测试），断言内带上 kind 作为失败信息（Go 的 `t.Run` 名字）。
// 10) 并发用例（TestSnapshotReleaseCompactionSupportsConcurrentReaders）的 goroutine + channel → `Thread` +
//     `ManualResetEventSlim`（先例 CheckerPoolTests.cs 文件头第 7 条）；Go 的 `<-readerError`（关闭的空 channel 取零值 nil）
//     落成线程结束后的 `Assert.Null(readerError)`。
// 11) `context.Background` → `Context.Background`；`lsproto.LanguageKindTypeScript` → `LanguageKind.TypeScript`；
//     `lsproto.TextDocumentContentChangePartialOrWholeDocument{WholeDocument: …}` →
//     `new TextDocumentContentChangePartialOrWholeDocument { WholeDocument = new TextDocumentContentChangeWholeDocument { Text = … } }`。
// 12) `defer`（`projectSession.Close()` / `session.Close()` / `clone.Deref()`）→ `try/finally`。
// 13) 其余用例里 Go 的 `defer projectSession.Close()`（`Setup` 起的项目会话）不移植：本仓测试的既有约定是只关 API 会话
//     （先例 Project/ConfigFileChangesTests.cs），对可观察行为无影响（见 SessionCreateProgramTests.cs 文件头第 13 条）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using TypeScript.Api;
using TypeScript.Api.RequestFileSystem;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using Path = TypeScript.TSPath.Path;
using ProjectNs = TypeScript.Project;
using ApiSession = TypeScript.Api.Session;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;

namespace TypeScript.Tests;

public class SessionRequestFileSystemTests
{
    /// <summary>Go: <c>func updateCurrentLanguageServerSnapshot(ctx, session, changes) (*CreateSnapshotResponse, error)</c>（session_requestfilesystem_test.go:19）。</summary>
    private static CreateSnapshotResponse UpdateCurrentLanguageServerSnapshot(Context ctx, ApiSession session, CreateSnapshotParams changes)
    {
        var lspChanges = new LanguageServerSnapshotChanges();
        CopyBaseFields(changes, lspChanges);
        var baseResponse = session.HandleGetCurrentLanguageServerSnapshot(ctx, new GetCurrentLanguageServerSnapshotParams
        {
            Changes = lspChanges,
        });

        var updateChanges = new CreateSnapshotParams();
        CopyBaseFields(changes, updateChanges);
        updateChanges.EnsurePrograms = new EnsurePrograms { All = true };
        updateChanges.FileSystem = changes.FileSystem;
        return session.HandleUpdateSnapshot(ctx, new UpdateSnapshotParams
        {
            Snapshot = baseResponse.Snapshot,
            Changes = updateChanges,
        });
    }

    /// <summary>Go 的内嵌结构体赋值（见文件头第 1 条）：把 8 个提升字段逐个搬过去。</summary>
    private static void CopyBaseFields(SnapshotRequestChangesParams src, SnapshotRequestChangesParams dst)
    {
        dst.OpenProjects = src.OpenProjects;
        dst.CloseProjects = src.CloseProjects;
        dst.OpenFiles = src.OpenFiles;
        dst.CloseFiles = src.CloseFiles;
        dst.CreatePrograms = src.CreatePrograms;
        dst.ReconfigurePrograms = src.ReconfigurePrograms;
        dst.RemovePrograms = src.RemovePrograms;
        dst.EnsurePrograms = src.EnsurePrograms;
    }

    private static ProjectNs.ConfiguredProjectID ConfigId(string path) => new(new Path(path));

    /// <summary>Go: <c>TestEditorChangeInvalidatesRequestSymlinkAlias</c>（session_requestfilesystem_test.go:37）。</summary>
    [Fact]
    public void EditorChangeInvalidatesRequestSymlinkAlias()
    {
        var ctx = Context.Background;
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["alias.ts"] }""",
        });
        projectSession.DidOpenFile(ctx, "file:///target.ts", 1, "old", LanguageKind.TypeScript);
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = UpdateCurrentLanguageServerSnapshot(ctx, session, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Layer,
                    Symlinks = new Dictionary<string, RequestSymlink> { ["/alias.ts"] = new RequestSymlink { Target = "/target.ts" } },
                },
            });
            Assert.Equal(
                "old",
                session._snapshots[baseResponse.Snapshot].Snapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!
                    .GetProgram()!.GetSourceFile("/alias.ts")!.Text());

            projectSession.DidChangeFile(ctx, "file:///target.ts", 2,
                [new TextDocumentContentChangePartialOrWholeDocument { WholeDocument = new TextDocumentContentChangeWholeDocument { Text = "new" } }]);
            var updated = UpdateCurrentLanguageServerSnapshot(ctx, session, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Layer,
                    Symlinks = new Dictionary<string, RequestSymlink> { ["/alias.ts"] = new RequestSymlink { Target = "/target.ts" } },
                },
            });
            Assert.Equal(
                "new",
                session._snapshots[updated.Snapshot].Snapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!
                    .GetProgram()!.GetSourceFile("/alias.ts")!.Text());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestLargeRequestLayerUpdateRetainsChanges</c>（session_requestfilesystem_test.go:74）。</summary>
    [Fact]
    public void LargeRequestLayerUpdateRetainsChanges()
    {
        const int fillerCount = 1000;
        var baseFiles = new Dictionary<string, string>(fillerCount);
        var updatedFiles = new Dictionary<string, string>(fillerCount + 1);
        for (var i = 0; i < fillerCount; i++)
        {
            var fileName = $"/unused/file{i.ToString(CultureInfo.InvariantCulture)}.ts";
            baseFiles[fileName] = "old";
            updatedFiles[fileName] = "new";
        }

        updatedFiles["/index.ts"] = "new";

        var ctx = Context.Background;
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["index.ts"] }""",
        });
        projectSession.DidOpenFile(ctx, "file:///index.ts", 1, "old", LanguageKind.TypeScript);
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = UpdateCurrentLanguageServerSnapshot(ctx, session, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                FileSystem = new RequestFileSystem { Kind = Kind.Layer, Files = baseFiles },
            });

            var updated = session.HandleUpdateSnapshot(ctx, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    EnsurePrograms = new EnsurePrograms { All = true },
                    FileSystem = new RequestFileSystem { Kind = Kind.Layer, Files = updatedFiles },
                },
            });
            Assert.True(session._snapshots[updated.Snapshot].Snapshot.TryReadFile("/index.ts", out var contents));
            Assert.Equal("new", contents);
            Assert.Equal(
                "new",
                session._snapshots[updated.Snapshot].Snapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!
                    .GetProgram()!.GetSourceFile("/index.ts")!.Text());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestAutoImportCloneRetainsRequestFileSystem</c>（session_requestfilesystem_test.go:116）。</summary>
    [Fact]
    public void AutoImportCloneRetainsRequestFileSystem()
    {
        var ctx = Context.Background;
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = UpdateCurrentLanguageServerSnapshot(ctx, session, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Full,
                    Files = new Dictionary<string, string> { ["/index.ts"] = "request" },
                },
            });
            var baseSnapshot = session._snapshots[baseResponse.Snapshot].Snapshot;

            var clone = session._snapshotHost.CloneSnapshotWithAutoImports(ctx, baseSnapshot, "file:///index.ts", null);
            try
            {
                Assert.True(clone.TryReadFile("/index.ts", out var content));
                Assert.Equal("request", content);
            }
            finally
            {
                clone.Deref();
            }
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestUnmaskedOverlayContinuesUpdating</c>（session_requestfilesystem_test.go:137）。</summary>
    [Fact]
    public void UnmaskedOverlayContinuesUpdating()
    {
        var ctx = Context.Background;
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["index.ts"] }""",
            ["/index.ts"] = "host",
        });
        projectSession.DidOpenFile(ctx, "file:///index.ts", 1, "overlay1", LanguageKind.TypeScript);
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var masked = UpdateCurrentLanguageServerSnapshot(ctx, session, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Layer,
                    Files = new Dictionary<string, string> { ["/index.ts"] = "request" },
                },
            });
            Assert.Equal(
                "request",
                session._snapshots[masked.Snapshot].Snapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!
                    .GetProgram()!.GetSourceFile("/index.ts")!.Text());

            var unmasked = UpdateCurrentLanguageServerSnapshot(ctx, session, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
            });
            Assert.Equal(
                "overlay1",
                session._snapshots[unmasked.Snapshot].Snapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!
                    .GetProgram()!.GetSourceFile("/index.ts")!.Text());

            projectSession.DidChangeFile(ctx, "file:///index.ts", 2,
                [new TextDocumentContentChangePartialOrWholeDocument { WholeDocument = new TextDocumentContentChangeWholeDocument { Text = "overlay2" } }]);
            var updated = UpdateCurrentLanguageServerSnapshot(ctx, session, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
            });
            Assert.Equal(
                "overlay2",
                session._snapshots[updated.Snapshot].Snapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!
                    .GetProgram()!.GetSourceFile("/index.ts")!.Text());
        }
        finally
        {
            session.Close();
            projectSession.Close();
        }
    }

    /// <summary>Go: <c>TestRequestHostMountReadsEditorOverlays</c>（session_requestfilesystem_test.go:172）：两个子测试（full / layer）。</summary>
    [Fact]
    public void RequestHostMountReadsEditorOverlays()
    {
        foreach (var kind in new[] { Kind.Full, Kind.Layer })
        {
            var ctx = Context.Background;
            var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
            {
                ["/host/index.ts"] = "host",
            });
            projectSession.DidOpenFile(ctx, "file:///host/index.ts", 1, "overlay", LanguageKind.TypeScript);
            projectSession.DidOpenFile(ctx, "file:///host/new.ts", 1, "new overlay", LanguageKind.TypeScript);
            var session = ApiSession.NewLSPSession(projectSession, null);
            try
            {
                var baseResponse = UpdateCurrentLanguageServerSnapshot(ctx, session, new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = kind,
                        Files = new Dictionary<string, string>
                        {
                            ["/host/index.ts"] = "request",
                            ["/host/new.ts"] = "request",
                        },
                        Symlinks = new Dictionary<string, RequestSymlink>
                        {
                            ["/mounted"] = new RequestSymlink { Target = "/host", Host = true },
                        },
                    },
                });
                var snapshot = session._snapshots[baseResponse.Snapshot].Snapshot;
                Assert.True(snapshot.TryReadFile("/mounted/index.ts", out var content), $"kind={kind}");
                Assert.Equal("overlay", content);
                Assert.True(snapshot.TryReadFile("/mounted/new.ts", out content), $"kind={kind}");
                Assert.Equal("new overlay", content);
            }
            finally
            {
                session.Close();
                projectSession.Close();
            }
        }
    }

    /// <summary>Go: <c>TestCreateSnapshotUsesFullFileSystem</c>（session_requestfilesystem_test.go:213）。</summary>
    [Fact]
    public void CreateSnapshotUsesFullFileSystem()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/host.ts"] = "host",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Full,
                    Files = new Dictionary<string, string>
                    {
                        ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["src/index.ts"] }""",
                        ["/src/index.ts"] = """export const value = "memory";""",
                        ["/src/other.ts"] = "export const other = true;",
                    },
                },
            });
            Assert.Single(response.Projects);
            Assert.Equal("/tsconfig.json", response.Projects[0]!.ConfigFileName);

            var snapshot = session._snapshots[response.Snapshot].Snapshot;
            Assert.True(snapshot.TryReadFile("/src/index.ts", out var contents));
            Assert.Equal("""export const value = "memory";""", contents);
            Assert.False(snapshot.TryReadFile("/host.ts", out _));

            // Carrying the same filesystem forward without a delta must preserve
            // incremental state instead of forcing a full program rebuild.
            var program = snapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!.GetProgram();
            var unchanged = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams { Snapshot = response.Snapshot });
            var unchangedSnapshot = session._snapshots[unchanged.Snapshot].Snapshot;
            Assert.Same(program, unchangedSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!.GetProgram());
            response = unchanged;

            // Supplying a new filesystem replaces inherited snapshot file caches even
            // when the caller does not redundantly list every file in FileChanges.
            response = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = response.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    EnsurePrograms = new EnsurePrograms { All = true },
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Full,
                        Files = new Dictionary<string, string>
                        {
                            ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["src/index.ts", "src/other.ts"] }""",
                            ["/src/index.ts"] = """export const value = "updated";""",
                            ["/src/other.ts"] = "export const other = true;",
                        },
                    },
                },
            });
            snapshot = session._snapshots[response.Snapshot].Snapshot;
            Assert.True(snapshot.TryReadFile("/src/index.ts", out contents));
            Assert.Equal("""export const value = "updated";""", contents);

            // A new layer retains the base snapshot's supplied filesystem for every file
            // other than its override.
            var temporary = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = response.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Layer,
                        Files = new Dictionary<string, string> { ["/src/index.ts"] = """export const value = "temporary";""" },
                    },
                },
            });
            var temporarySnapshot = session._snapshots[temporary.Snapshot].Snapshot;
            Assert.True(temporarySnapshot.TryReadFile("/src/index.ts", out contents));
            Assert.Equal("""export const value = "temporary";""", contents);
            Assert.True(temporarySnapshot.TryReadFile("/src/other.ts", out contents));
            Assert.Equal("export const other = true;", contents);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestUpdateSnapshotRequestFileOverridesOpenOverlay</c>（session_requestfilesystem_test.go:298）。</summary>
    [Fact]
    public void UpdateSnapshotRequestFileOverridesOpenOverlay()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/index.ts"] = "host",
        });
        projectSession.DidOpenFile(Context.Background, "file:///index.ts", 1, "overlay", LanguageKind.TypeScript);

        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Layer,
                    Files = new Dictionary<string, string> { ["/index.ts"] = "request" },
                },
            });

            var snapshot = session._snapshots[response.Snapshot].Snapshot;
            Assert.True(snapshot.TryReadFile("/index.ts", out var contents));
            Assert.Equal("request", contents);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestUpdateSnapshotRequestTombstoneRemovesOpenOverlay</c>（session_requestfilesystem_test.go:330）。</summary>
    [Fact]
    public void UpdateSnapshotRequestTombstoneRemovesOpenOverlay()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/index.ts"] = "host",
        });
        projectSession.DidOpenFile(Context.Background, "file:///index.ts", 1, "overlay", LanguageKind.TypeScript);

        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Layer,
                    RemovedPaths = ["/index.ts"],
                },
            });

            var snapshot = session._snapshots[response.Snapshot].Snapshot;
            Assert.False(snapshot.TryReadFile("/index.ts", out _));
            Assert.Null(snapshot.GetDefaultProject("file:///index.ts"));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestUpdateSnapshotRequestTombstoneRemovesHostlessOpenOverlay</c>（session_requestfilesystem_test.go:360）。</summary>
    [Fact]
    public void UpdateSnapshotRequestTombstoneRemovesHostlessOpenOverlay()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        projectSession.DidOpenFile(Context.Background, "file:///index.ts", 1, "overlay", LanguageKind.TypeScript);

        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Layer,
                    RemovedPaths = ["/index.ts"],
                },
            });

            var snapshot = session._snapshots[response.Snapshot].Snapshot;
            Assert.False(snapshot.TryReadFile("/index.ts", out _));
            Assert.Null(snapshot.GetDefaultProject("file:///index.ts"));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestUpdateSnapshotRequestFileMasksHostlessOpenOverlayDirectory</c>（session_requestfilesystem_test.go:383）。</summary>
    [Fact]
    public void UpdateSnapshotRequestFileMasksHostlessOpenOverlayDirectory()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        projectSession.DidOpenFile(Context.Background, "file:///src/index.ts", 1, "overlay", LanguageKind.TypeScript);

        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Layer,
                    Files = new Dictionary<string, string> { ["/src"] = "request" },
                },
            });

            var snapshot = session._snapshots[response.Snapshot].Snapshot;
            Assert.True(snapshot.TryReadFile("/src", out var contents));
            Assert.Equal("request", contents);
            Assert.False(snapshot.TryReadFile("/src/index.ts", out _));
            Assert.Null(snapshot.GetDefaultProject("file:///src/index.ts"));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestUpdateSnapshotRequestMaskUpdatesOpenConfiguredProjects</c>（session_requestfilesystem_test.go:407）。</summary>
    [Fact]
    public void UpdateSnapshotRequestMaskUpdatesOpenConfiguredProjects()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["index.ts"] }""",
            ["/index.ts"] = "host",
        });
        projectSession.DidOpenFile(Context.Background, "file:///index.ts", 1, "overlay", LanguageKind.TypeScript);

        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                },
            });
            var baseSnapshot = session._snapshots[baseResponse.Snapshot].Snapshot;
            Assert.True(baseSnapshot.ProjectCollection.GetOpenConfiguredProjects().Has(ConfigId("/tsconfig.json")));

            var masked = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Layer,
                        Files = new Dictionary<string, string> { ["/index.ts"] = "request" },
                    },
                },
            });
            var maskedSnapshot = session._snapshots[masked.Snapshot].Snapshot;
            Assert.False(maskedSnapshot.ProjectCollection.GetOpenConfiguredProjects().Has(ConfigId("/tsconfig.json")));

            var unmasked = session.HandleGetCurrentLanguageServerSnapshot(Context.Background, new GetCurrentLanguageServerSnapshotParams
            {
                Changes = new LanguageServerSnapshotChanges
                {
                    OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                },
            });
            var unmaskedSnapshot = session._snapshots[unmasked.Snapshot].Snapshot;
            Assert.True(unmaskedSnapshot.ProjectCollection.GetOpenConfiguredProjects().Has(ConfigId("/tsconfig.json")));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestUpdateSnapshotConfigChangeSkipsMaskedOpenOverlay</c>（session_requestfilesystem_test.go:448）。</summary>
    [Fact]
    public void UpdateSnapshotConfigChangeSkipsMaskedOpenOverlay()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/index.ts"] = "host",
        });
        projectSession.DidOpenFile(Context.Background, "file:///index.ts", 1, "overlay", LanguageKind.TypeScript);

        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams());

            var updated = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Layer,
                        Files = new Dictionary<string, string>
                        {
                            ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["index.ts"] }""",
                        },
                        RemovedPaths = ["/index.ts"],
                    },
                },
            });

            var snapshot = session._snapshots[updated.Snapshot].Snapshot;
            Assert.False(snapshot.TryReadFile("/index.ts", out _));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCreateProgramRetainsFullFileSystem</c>（session_requestfilesystem_test.go:483）。</summary>
    [Fact]
    public void CreateProgramRetainsFullFileSystem()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var ctx = Context.Background;
            var baseResponse = session.HandleCreateSnapshot(ctx, new CreateSnapshotParams
            {
                OpenFiles = [new DocumentIdentifier { FileName = "/old.ts" }],
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Full,
                    Files = new Dictionary<string, string>
                    {
                        ["/old.ts"] = "export const oldValue = 1;",
                        ["/new.ts"] = "export const newValue = 2;",
                    },
                },
            });
            Assert.Single(baseResponse.Projects);

            var created = session.HandleUpdateSnapshot(ctx, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    CreatePrograms =
                    [
                        new CreateSnapshotProgramParams
                        {
                            RootFiles = [new DocumentIdentifier { FileName = "/new.ts" }],
                            CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                        },
                    ],
                },
            });

            var snapshot = session.GetSnapshotData(created.Snapshot);
            var program = snapshot.GetProgram(created.Operation!.CreatedPrograms![0].AsID());
            Assert.NotNull(program.GetSourceFile("/new.ts"));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestSnapshotUpdateFullFileSystemIsTotal</c>（session_requestfilesystem_test.go:518）。</summary>
    [Fact]
    public void SnapshotUpdateFullFileSystemIsTotal()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/host.ts"] = "host",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams());
            var replaced = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Full,
                        Files = new Dictionary<string, string> { ["/memory.ts"] = "memory" },
                    },
                },
            });

            var snapshot = session._snapshots[replaced.Snapshot].Snapshot;
            Assert.True(snapshot.TryReadFile("/memory.ts", out var contents));
            Assert.Equal("memory", contents);
            Assert.False(snapshot.TryReadFile("/host.ts", out _));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestSnapshotUpdateCarriesHostFileSystemWithoutOverride</c>（session_requestfilesystem_test.go:547）。</summary>
    [Fact]
    public void SnapshotUpdateCarriesHostFileSystemWithoutOverride()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["index.ts"] }""",
            ["/index.ts"] = "export const value = true;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
            });
            var baseSnapshot = session._snapshots[baseResponse.Snapshot].Snapshot;
            var program = baseSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!.GetProgram();

            var updated = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams { Snapshot = baseResponse.Snapshot });
            var updatedSnapshot = session._snapshots[updated.Snapshot].Snapshot;
            Assert.Same(program, updatedSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!.GetProgram());
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestSnapshotFileSystemLayersPreserveIncrementalState</c>（session_requestfilesystem_test.go:571）：三个子测试（host / full / layer）。</summary>
    [Fact]
    public void SnapshotFileSystemLayersPreserveIncrementalState()
    {
        foreach (var baseKind in new[] { "host", "full", "layer" })
        {
            var files = new Dictionary<string, string>
            {
                ["/a/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "include": ["**/*.ts"] }""",
                ["/a/index.ts"] = "export const value = 1;",
                ["/a/removed/nested.ts"] = "export const nested = true;",
                ["/a/removed/deep/file.ts"] = "export const deep = true;",
                ["/b/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["index.ts"] }""",
                ["/b/index.ts"] = "export const unrelated = true;",
            };
            var hostFiles = new Dictionary<string, object?>(files.Count);
            foreach (var (path, content) in files)
            {
                hostFiles[path] = content;
            }

            var (projectSession, _) = Harness.Setup(hostFiles);
            var session = ApiSession.NewLSPSession(projectSession, null);
            try
            {
                var ctx = Context.Background;
                var parameters = new CreateSnapshotParams
                {
                    OpenProjects =
                    [
                        new DocumentIdentifier { FileName = "/a/tsconfig.json" },
                        new DocumentIdentifier { FileName = "/b/tsconfig.json" },
                    ],
                };
                if (baseKind != "host")
                {
                    parameters.FileSystem = new RequestFileSystem { Kind = new Kind(baseKind), Files = files };
                }

                var baseResponse = session.HandleCreateSnapshot(ctx, parameters);
                var baseSnapshot = session._snapshots[baseResponse.Snapshot].Snapshot;
                var baseProgram = baseSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/a/tsconfig.json"))!.GetProgram();
                var unrelatedProgram = baseSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/b/tsconfig.json"))!.GetProgram();
                var unrelatedFile = baseSnapshot.GetFile("/b/index.ts");

                var unchanged = session.HandleUpdateSnapshot(ctx, new UpdateSnapshotParams
                {
                    Snapshot = baseResponse.Snapshot,
                    Changes = new CreateSnapshotParams
                    {
                        EnsurePrograms = new EnsurePrograms { All = true },
                        FileSystem = new RequestFileSystem
                        {
                            Kind = Kind.Layer,
                            Files = new Dictionary<string, string> { ["/a/index.ts"] = files["/a/index.ts"] },
                        },
                    },
                });
                var unchangedSnapshot = session._snapshots[unchanged.Snapshot].Snapshot;
                Assert.Same(baseProgram, unchangedSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/a/tsconfig.json"))!.GetProgram());
                Assert.Same(unrelatedProgram, unchangedSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/b/tsconfig.json"))!.GetProgram());
                Assert.Same(unrelatedFile, unchangedSnapshot.GetFile("/b/index.ts"));

                const string updatedText = "export const value = 2;";
                var updated = session.HandleUpdateSnapshot(ctx, new UpdateSnapshotParams
                {
                    Snapshot = unchanged.Snapshot,
                    Changes = new CreateSnapshotParams
                    {
                        EnsurePrograms = new EnsurePrograms { All = true },
                        FileSystem = new RequestFileSystem
                        {
                            Kind = Kind.Layer,
                            Files = new Dictionary<string, string> { ["/a/index.ts"] = updatedText },
                        },
                    },
                });
                var updatedSnapshot = session._snapshots[updated.Snapshot].Snapshot;
                var updatedProject = updatedSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/a/tsconfig.json"))!;
                Assert.NotSame(baseProgram, updatedProject.GetProgram());
                Assert.Equal(ProjectNs.ProgramUpdateKind.ProgramUpdateKindCloned, updatedProject.ProgramUpdateKind);
                Assert.Equal(updatedText, updatedProject.GetProgram()!.GetSourceFile("/a/index.ts")!.Text());
                Assert.Same(unrelatedProgram, updatedSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/b/tsconfig.json"))!.GetProgram());
                Assert.Same(unrelatedFile, updatedSnapshot.GetFile("/b/index.ts"));

                var removed = session.HandleUpdateSnapshot(ctx, new UpdateSnapshotParams
                {
                    Snapshot = updated.Snapshot,
                    Changes = new CreateSnapshotParams
                    {
                        EnsurePrograms = new EnsurePrograms { All = true },
                        FileSystem = new RequestFileSystem
                        {
                            Kind = Kind.Layer,
                            RemovedPaths = ["/a/removed"],
                        },
                    },
                });
                var removedSnapshot = session._snapshots[removed.Snapshot].Snapshot;
                var removedProgram = removedSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/a/tsconfig.json"))!.GetProgram();
                foreach (var path in new[] { "/a/removed/nested.ts", "/a/removed/deep/file.ts" })
                {
                    Assert.Null(removedProgram!.GetSourceFile(path));
                    Assert.Null(removedSnapshot.GetFile(path));
                    Assert.NotNull(baseProgram!.GetSourceFile(path));
                }

                Assert.Same(unrelatedProgram, removedSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/b/tsconfig.json"))!.GetProgram());
                Assert.Same(unrelatedFile, removedSnapshot.GetFile("/b/index.ts"));

                // A request without a base snapshot returns to the host, so the old
                // layer's changed contents and directory tombstones must not survive.
                var restoredChanges = new CreateSnapshotParams();
                CopyBaseFields(parameters, restoredChanges);
                var restored = session.HandleCreateSnapshot(ctx, restoredChanges);
                var restoredSnapshot = session._snapshots[restored.Snapshot].Snapshot;
                Assert.False(restoredSnapshot.HasFileSystemOverride(), $"baseKind={baseKind}");
                var restoredProgram = restoredSnapshot.ProjectCollection.GetProject(new ProjectNs.ID("/a/tsconfig.json"))!.GetProgram();
                Assert.Equal(files["/a/index.ts"], restoredProgram!.GetSourceFile("/a/index.ts")!.Text());
                Assert.NotNull(restoredProgram.GetSourceFile("/a/removed/deep/file.ts"));
            }
            finally
            {
                session.Close();
                projectSession.Close();
            }
        }
    }

    /// <summary>Go: <c>TestSnapshotFileSystemLayerWithoutBaseUpdatesHostState</c>（session_requestfilesystem_test.go:676）。</summary>
    [Fact]
    public void SnapshotFileSystemLayerWithoutBaseUpdatesHostState()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true }, "files": ["index.ts"] }""",
            ["/index.ts"] = "export const value = 1;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var ctx = Context.Background;
            session.HandleCreateSnapshot(ctx, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
            });

            const string updatedText = "export const value = 2;";
            var updated = session.HandleCreateSnapshot(ctx, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Layer,
                    Files = new Dictionary<string, string> { ["/index.ts"] = updatedText },
                },
            });
            var snapshot = session._snapshots[updated.Snapshot].Snapshot;
            var updatedProject = snapshot.ProjectCollection.GetProject(new ProjectNs.ID("/tsconfig.json"))!;
            Assert.Equal(updatedText, updatedProject.GetProgram()!.GetSourceFile("/index.ts")!.Text());
            Assert.Equal(ProjectNs.ProgramUpdateKind.ProgramUpdateKindNewFiles, updatedProject.ProgramUpdateKind);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestEmitFromLayerOverFullFileSystemReturnsFileContents</c>（session_requestfilesystem_test.go:714）。</summary>
    [Fact]
    public void EmitFromLayerOverFullFileSystemReturnsFileContents()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var ctx = Context.Background;

            var baseResponse = session.HandleCreateSnapshot(ctx, new CreateSnapshotParams
            {
                OpenProjects = [new DocumentIdentifier { FileName = "/tsconfig.json" }],
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Full,
                    Files = new Dictionary<string, string>
                    {
                        ["/tsconfig.json"] = """{ "compilerOptions": { "noLib": true, "outDir": "/out" }, "files": ["src/main.ts"] }""",
                        ["/src/main.ts"] = "export const value: number = 1;",
                    },
                },
            });
            var layered = session.HandleUpdateSnapshot(ctx, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Layer,
                        Files = new Dictionary<string, string>(),
                    },
                },
            });
            Assert.Empty(layered.Projects);

            var emitted = session.HandleEmit(ctx, new EmitParams
            {
                Snapshot = layered.Snapshot,
                Project = baseResponse.Projects[0]!.Id,
            });
            Assert.Equal(new[] { "/out/src/main.js" }, emitted.EmittedFiles);
            Assert.Equal(new[] { "export const value = 1;\n" }, emitted.EmittedFilesContents);

            session.HandleRelease(ctx, new ReleaseParams { Snapshot = baseResponse.Snapshot });
            var emittedAfterRelease = session.HandleEmit(ctx, new EmitParams
            {
                Snapshot = layered.Snapshot,
                Project = baseResponse.Projects[0]!.Id,
            });
            Assert.Equal(emitted.EmittedFiles, emittedAfterRelease.EmittedFiles);
            Assert.Equal(emitted.EmittedFilesContents, emittedAfterRelease.EmittedFilesContents);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestReleaseSnapshotCompactsSoleLayeredFileSystem</c>（session_requestfilesystem_test.go:748）。</summary>
    [Fact]
    public void ReleaseSnapshotCompactsSoleLayeredFileSystem()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/host.ts"] = "host",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Full,
                    Files = new Dictionary<string, string>
                    {
                        ["/inherited.ts"] = "inherited",
                        ["/changed.ts"] = "old",
                        ["/removed.ts"] = "removed",
                    },
                },
            });
            var baseFileSystem = session._snapshots[baseResponse.Snapshot].FileSystem;
            Assert.NotNull(baseFileSystem);

            var layered = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Layer,
                        Files = new Dictionary<string, string>
                        {
                            ["/changed.ts"] = "new",
                            ["/added.ts"] = "added",
                        },
                        RemovedPaths = ["/removed.ts"],
                    },
                },
            });
            var layeredSnapshotData = session._snapshots[layered.Snapshot];
            var layeredSnapshot = layeredSnapshotData.Snapshot;
            var layeredFileSystem = layeredSnapshotData.FileSystem;
            Assert.NotNull(layeredFileSystem);
            Assert.Equal(1, session._snapshots[baseResponse.Snapshot].RefCount);

            session.HandleRelease(Context.Background, new ReleaseParams { Snapshot = baseResponse.Snapshot });
            Assert.False(session._snapshots.ContainsKey(baseResponse.Snapshot));

            foreach (var (path, expected) in new Dictionary<string, string>
            {
                ["/inherited.ts"] = "inherited",
                ["/changed.ts"] = "new",
                ["/added.ts"] = "added",
            })
            {
                Assert.True(layeredSnapshot.TryReadFile(path, out var contents), path);
                Assert.Equal(expected, contents);
            }

            Assert.False(layeredSnapshot.TryReadFile("/removed.ts", out _));
            Assert.False(layeredSnapshot.TryReadFile("/host.ts", out _));
            Assert.True(RequestFileSystem.HasFullFileSystem(layeredFileSystem));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestEagerSnapshotReleaseDoesNotRetainFileSystemHistory</c>（session_requestfilesystem_test.go:806）。</summary>
    [Fact]
    public void EagerSnapshotReleaseDoesNotRetainFileSystemHistory()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Full,
                    Files = new Dictionary<string, string> { ["/pkg/index.ts"] = "" },
                },
            });

            var content = "";
            foreach (var character in "export const x = 1")
            {
                var oldSnapshot = response.Snapshot;
                content += character;
                response = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
                {
                    Snapshot = oldSnapshot,
                    Changes = new CreateSnapshotParams
                    {
                        FileSystem = new RequestFileSystem
                        {
                            Kind = Kind.Layer,
                            Files = new Dictionary<string, string> { ["/pkg/index.ts"] = content },
                        },
                    },
                });
                session.HandleRelease(Context.Background, new ReleaseParams { Snapshot = oldSnapshot });

                Assert.Single(session._snapshots);
                var current = session._snapshots[response.Snapshot];
                Assert.NotNull(current);
                Assert.Equal(1, current.RefCount);
                var fileSystem = current.FileSystem;
                Assert.NotNull(fileSystem);
                Assert.True(RequestFileSystem.HasFullFileSystem(fileSystem));
                Assert.True(current.Snapshot.TryReadFile("/pkg/index.ts", out var actual));
                Assert.Equal(content, actual);
            }
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestSnapshotReleaseCompactsChainedFileSystems</c>（session_requestfilesystem_test.go:848）。</summary>
    [Fact]
    public void SnapshotReleaseCompactsChainedFileSystems()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var responses = new CreateSnapshotResponse[4];
            responses[0] = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Full,
                    Files = new Dictionary<string, string> { ["/pkg/index.ts"] = "0" },
                },
            });
            for (var i = 1; i < responses.Length; i++)
            {
                responses[i] = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
                {
                    Snapshot = responses[i - 1].Snapshot,
                    Changes = new CreateSnapshotParams
                    {
                        FileSystem = new RequestFileSystem
                        {
                            Kind = Kind.Layer,
                            Files = new Dictionary<string, string> { ["/pkg/index.ts"] = i.ToString(CultureInfo.InvariantCulture) },
                        },
                    },
                });
            }

            session.HandleRelease(Context.Background, new ReleaseParams { Snapshot = responses[0].Snapshot });
            Assert.False(session._snapshots.ContainsKey(responses[0].Snapshot));

            for (var i = 1; i < responses.Length; i++)
            {
                var current = session._snapshots[responses[i].Snapshot];
                Assert.NotNull(current);
                Assert.Equal(1, current.RefCount);
                var fileSystem = current.FileSystem;
                Assert.NotNull(fileSystem);
                Assert.True(RequestFileSystem.HasFullFileSystem(fileSystem));
                Assert.True(current.Snapshot.TryReadFile("/pkg/index.ts", out var contents));
                Assert.Equal(i.ToString(CultureInfo.InvariantCulture), contents);
            }
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestTemporarySnapshotRetainsLayeredFileSystemHistory</c>（session_requestfilesystem_test.go:896）。</summary>
    [Fact]
    public void TemporarySnapshotRetainsLayeredFileSystemHistory()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var baseResponse = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem
                {
                    Kind = Kind.Full,
                    Files = new Dictionary<string, string> { ["/pkg/index.ts"] = "base" },
                },
            });
            var layered = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Layer,
                        Files = new Dictionary<string, string> { ["/pkg/index.ts"] = "layered" },
                    },
                },
            });
            var temporary = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = layered.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Layer,
                        Files = new Dictionary<string, string> { ["/pkg/index.ts"] = "temporary" },
                    },
                },
            });

            session.HandleRelease(Context.Background, new ReleaseParams { Snapshot = layered.Snapshot });
            session.HandleRelease(Context.Background, new ReleaseParams { Snapshot = baseResponse.Snapshot });

            var current = session._snapshots[temporary.Snapshot];
            Assert.NotNull(current);
            var fileSystem = current.FileSystem;
            Assert.NotNull(fileSystem);
            Assert.True(RequestFileSystem.HasFullFileSystem(fileSystem));
            Assert.True(current.Snapshot.TryReadFile("/pkg/index.ts", out var contents));
            Assert.Equal("temporary", contents);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestSnapshotReleaseCompactionSupportsConcurrentReaders</c>（session_requestfilesystem_test.go:949）。</summary>
    [Fact]
    public void SnapshotReleaseCompactionSupportsConcurrentReaders()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var files = new Dictionary<string, string>(1024);
            for (var index = 0; index < 1024; index++)
            {
                files[$"/pkg/file{index.ToString(CultureInfo.InvariantCulture)}.ts"] = index.ToString(CultureInfo.InvariantCulture);
            }

            var baseResponse = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                FileSystem = new RequestFileSystem { Kind = Kind.Full, Files = files },
            });
            var layered = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = baseResponse.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileSystem = new RequestFileSystem
                    {
                        Kind = Kind.Layer,
                        Files = new Dictionary<string, string> { ["/pkg/file0.ts"] = "updated" },
                    },
                },
            });
            var fileSystem = session._snapshots[layered.Snapshot].FileSystem;

            using var started = new ManualResetEventSlim(false);
            using var done = new ManualResetEventSlim(false);
            Exception? readerError = null;
            var reader = new Thread(() =>
            {
                started.Set();
                while (!done.IsSet)
                {
                    if (!fileSystem.TryReadFile("/pkg/file0.ts", out var contents) || contents != "updated")
                    {
                        readerError = new Exception($"unexpected overridden file: \"{contents}\"");
                        return;
                    }

                    if (!fileSystem.FileExists("/pkg/file1023.ts"))
                    {
                        readerError = new Exception("inherited file disappeared");
                        return;
                    }
                }
            });
            reader.Start();
            started.Wait();
            session.HandleRelease(Context.Background, new ReleaseParams { Snapshot = baseResponse.Snapshot });
            done.Set();
            reader.Join();
            Assert.Null(readerError);
        }
        finally
        {
            session.Close();
        }
    }
}
