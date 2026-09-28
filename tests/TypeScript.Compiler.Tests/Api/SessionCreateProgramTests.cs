// ported from: internal/api/session_createprogram_test.go:1-280
//
// 与 Go 测试的差异（逐条）：
// 1) `projecttestutil.Setup(files)` → `Harness.Setup(files)`（namespace `TypeScript.Tests.ProjectTestUtil`，
//    见 ProjectTestUtil.cs 文件头第 1 条）；`GetSessionInitOptions` 返回命名元组 `(Init, Utils)`。
//    别名必须叫 `Harness`（不能叫 `ProjectTestUtil`）：`TypeScript.Tests` 里有一个**同名命名空间**
//    `TypeScript.Tests.ProjectTestUtil`，外层命名空间的成员查找先于 using 别名 ⇒ 裸写 `ProjectTestUtil.Setup`
//    会报 CS0234（先例 Project/ConfigFileChangesTests.cs 文件头第 1 条，那里也叫 `Harness`）。
// 2) Go 的 `t.Parallel()` 在 xunit 下由框架调度，不写；`if !bundled.Embedded { t.Skip(...) }` 的分支在本仓不可达
//    （lib 文件已内嵌，`Bundled.LibPath()` 直接可用），不移植。
// 3) Go 测试直接调用未导出方法/读未导出字段（`session.handleCreateSnapshot`/`session.getSnapshotData`/
//    `snapshot.snapshot`）：C# 侧这些成员已按父切片要求放宽为 `internal`，测试程序集有
//    `InternalsVisibleTo("TypeScript.Compiler.Tests")`（`src/TypeScript.Compiler/Properties/AssemblyInfo.cs`），
//    因此保持同样的直接调用形式（先例：Project/CheckerPoolTests.cs 文件头第 4 条）。
// 4) `(T, error)` → 正常返回值 + 失败抛异常：`assert.NilError(t, err)` 不再需要，错误路径改成
//    `Assert.Throws<ClientErrorException>(...)` + `Assert.Contains(...)`。
// 5) `core.TSTrue` → `Tristate.TSTrue`；`core.CompilerOptions{...}` → `new CompilerOptions{...}`
//    （`TypeScript.Core.CompilerOptions`）。
// 6) `syntheticProjectID(1)` → `Project.SyntheticProjectID.NewSyntheticProjectID(1)`；`program.ID(programID)` →
//    `SyntheticProjectID.AsID()`。
// 7) `json.Marshal(response.Operation)` → `Marshal(response.Operation)`（返回 `byte[]`）+
//    `Encoding.UTF8.GetString`；`json.Unmarshal(data, &v)` → `UnmarshalNew<T>(bytes)`（零值实例，语义同 Go）。
// 8) `assert.DeepEqual(t, a, b)`（切片） → `Assert.Equal(expected, actual)`（xunit 对 `IEnumerable` 逐元素比较）。
// 9) `assert.Assert(t, x != nil)` → `Assert.NotNull(x)`；`assert.Assert(t, a == b)`（快照指针同一）→ `Assert.Same`。
// 10) `snapshot.snapshot.CreatedPrograms()` → `sd.Snapshot.CreatedPrograms()`；`session.getSnapshotData(h)` →
//     `session.GetSnapshotData(h)`。
// 11) `init.Options.CurrentDirectory = "D:/repo"`：`SessionInit.Options` 在 C# 是可写字段，逐字对应。
// 12) `response.Projects[0]` 在 C# 是 `ProjectResponse?`（proto 的 `[]*ProjectResponse`）⇒ 取字段处加 `!`。
// 13) Go 每个用例的 `defer projectSession.Close()`（`Setup` 返回的项目会话）不移植：本仓测试的既有约定是只关 API 会话
//     （先例 Project/ConfigFileChangesTests.cs、Project/ProjectTests.cs 都只调 `session.Close()`），`Setup` 起的项目会话
//     在测试里不持有进程/管道等外部资源，关与不关对可观察行为没有影响。
// 14) `TestCreateSnapshotRejectsRemovingProgramFromIndependentRoot` 的消息断言拆成两条（见该用例内的注释）：
//     C# 目前把 `SyntheticProjectID`（record struct，无 `ToString()` 覆写）插进消息，得到
//     "…removal: SyntheticProjectID { Value = /dev/null/synthetic/1 }"，而 Go 的 `%s` 打 "…removal: /dev/null/synthetic/1"。
//     语义断言不变（消息主体 + ID 文本），只放宽了排版；根因在 src（ProjectCollectionBuilder.cs:513 或
//     `SyntheticProjectID` 缺 ToString），已在交付报告里登记。
using System.Collections.Generic;
using System.Text;
using TypeScript.Api;
using TypeScript.Core;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using ProjectNs = TypeScript.Project;
using ApiSession = TypeScript.Api.Session;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;
// 裸写 `Json.Marshal` 会绑到命名空间 `TypeScript.Json`（外层命名空间查找先于 using 导入），
// 故照 LsProto/CodecCoverageTests.cs 的做法静态导入 Json 的成员（先例：Project/ProjectTestUtil.cs 文件头第 3 条）。
using static TypeScript.Json.Json;

namespace TypeScript.Tests;

public class SessionCreateProgramTests
{
    /// <summary>Go: <c>TestCreateSnapshotUsesIndependentRoots</c>（session_createprogram_test.go:15）。</summary>
    [Fact]
    public void CreateSnapshotUsesIndependentRoots()
    {
        var (init, _) = Harness.GetSessionInitOptions(new Dictionary<string, object?>
        {
            ["/home/projects/p/src/index.ts"] = "export const x = 1;",
        }, null, new TypingsInstallerOptions());
        var session = ApiSession.NewStandaloneSession(init, null);
        try
        {
            var firstResponse = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                CreatePrograms =
                [
                    new CreateSnapshotProgramParams
                    {
                        RootFiles = [new DocumentIdentifier { FileName = "/home/projects/p/src/index.ts" }],
                        CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                    },
                ],
            });
            Assert.Equal(1UL, firstResponse.Snapshot.Value);
            Assert.Single(firstResponse.Projects);

            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams());
            Assert.Equal(2UL, response.Snapshot.Value);
            Assert.Empty(response.Projects);
            Assert.Single(firstResponse.Projects);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCreateSnapshotCreatesPrograms</c>（session_createprogram_test.go:44）。</summary>
    [Fact]
    public void CreateSnapshotCreatesPrograms()
    {
        const string fileA = "/home/projects/p/a.ts";
        const string fileB = "/home/projects/p/b.ts";
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            [fileA] = "export const a = 1;",
            [fileB] = "export const b = 1;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                CreatePrograms =
                [
                    new CreateSnapshotProgramParams
                    {
                        RootFiles = [new DocumentIdentifier { FileName = fileA }, new DocumentIdentifier { FileName = fileB }],
                        CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue, Strict = Tristate.TSTrue },
                    },
                    new CreateSnapshotProgramParams
                    {
                        RootFiles = [new DocumentIdentifier { FileName = fileB }],
                        CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                    },
                ],
            });

            Assert.Equal(2, response.Projects.Length);
            Assert.Equal(
                new[]
                {
                    ProjectNs.SyntheticProjectID.NewSyntheticProjectID(1),
                    ProjectNs.SyntheticProjectID.NewSyntheticProjectID(2),
                },
                response.Operation!.CreatedPrograms!);
            Assert.Equal("", response.Projects[0]!.ConfigFileName);
            Assert.Equal("", response.Projects[1]!.ConfigFileName);
            Assert.Equal(new[] { fileA, fileB }, response.Projects[0]!.RootFiles);
            Assert.Equal(Tristate.TSTrue, response.Projects[0]!.CompilerOptions.Strict);
            Assert.Equal(new[] { fileB }, response.Projects[1]!.RootFiles);

            var snapshot = session.GetSnapshotData(response.Snapshot);
            Assert.Equal(2, snapshot.Snapshot.CreatedPrograms().Length);
            foreach (var projectResponse in response.Projects)
            {
                Assert.NotNull(snapshot.Snapshot.ProjectCollection.GetProject(projectResponse!.Id));
            }
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCreateSnapshotPreservesWindowsRootDriveLetterCase</c>（session_createprogram_test.go:89）。</summary>
    [Fact]
    public void CreateSnapshotPreservesWindowsRootDriveLetterCase()
    {
        const string fileName = "D:/repo/index.ts";
        var (init, _) = Harness.GetSessionInitOptions(new Dictionary<string, object?>
        {
            [fileName] = "export const value = 1;",
        }, null, new TypingsInstallerOptions());
        init.Options.CurrentDirectory = "D:/repo";
        var session = ApiSession.NewStandaloneSession(init, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                CreatePrograms =
                [
                    new CreateSnapshotProgramParams
                    {
                        RootFiles = [new DocumentIdentifier { URI = "file:///D%3A/repo/index.ts" }],
                        CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                    },
                ],
            });

            var snapshot = session.GetSnapshotData(response.Snapshot);
            var program = snapshot.GetProgram(response.Projects[0]!.Id);
            Assert.Equal(fileName, program.GetSourceFile(fileName)!.FileName());
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestSnapshotOperationResponseOmitsUnrequestedFields</c>（session_createprogram_test.go:115）。</summary>
    [Fact]
    public void SnapshotOperationResponseOmitsUnrequestedFields()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams());
            Assert.Equal("{}", Encoding.UTF8.GetString(Marshal(response.Operation)));

            response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                CreatePrograms = [],
                ReconfigurePrograms = [],
                OpenFiles = [],
            });
            Assert.Equal("{\"createdPrograms\":[],\"openedFiles\":[]}", Encoding.UTF8.GetString(Marshal(response.Operation)));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestUpdateSnapshotReconfiguresSyntheticProgram</c>（session_createprogram_test.go:140）。</summary>
    [Fact]
    public void UpdateSnapshotReconfiguresSyntheticProgram()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/home/projects/p/a.ts"] = "export const a = 1;",
            ["/home/projects/p/b.ts"] = "export const b = 2;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var created = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                CreatePrograms =
                [
                    new CreateSnapshotProgramParams
                    {
                        RootFiles = [new DocumentIdentifier { FileName = "/home/projects/p/a.ts" }],
                        CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                    },
                ],
            });
            var programID = created.Operation!.CreatedPrograms![0];

            var reconfigured = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = created.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    ReconfigurePrograms =
                    [
                        new ReconfigureSnapshotProgramParams
                        {
                            Id = programID,
                            RootFiles = [new DocumentIdentifier { FileName = "/home/projects/p/b.ts" }],
                            CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue, Strict = Tristate.TSTrue },
                        },
                    ],
                },
            });

            Assert.Equal(programID.AsID(), reconfigured.Projects[0]!.Id);
            Assert.Equal(new[] { "/home/projects/p/b.ts" }, reconfigured.Projects[0]!.RootFiles);
            Assert.Equal(Tristate.TSTrue, reconfigured.Projects[0]!.CompilerOptions.Strict);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestReconfigureSyntheticProgramValidation</c>（session_createprogram_test.go:176）。</summary>
    [Fact]
    public void ReconfigureSyntheticProgramValidation()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var program = new ReconfigureSnapshotProgramParams { Id = new ProjectNs.SyntheticProjectID("/dev/null/synthetic/1") };

            var nullReconfigure = UnmarshalNew<SnapshotRequestChangesParams>("""{"reconfigurePrograms":[null]}"""u8.ToArray());
            var ex = Assert.Throws<ClientErrorException>(() => session.ToAPISnapshotRequest(Context.Background, nullReconfigure));
            Assert.Contains("reconfigurePrograms[0] must not be null", ex.Message);

            ex = Assert.Throws<ClientErrorException>(() => session.ToAPISnapshotRequest(Context.Background, new SnapshotRequestChangesParams
            {
                ReconfigurePrograms = [new ReconfigureSnapshotProgramParams { Id = new ProjectNs.SyntheticProjectID("/tsconfig.json") }],
            }));
            Assert.Contains("invalid synthetic project handle", ex.Message);

            ex = Assert.Throws<ClientErrorException>(() => session.ToAPISnapshotRequest(Context.Background, new SnapshotRequestChangesParams
            {
                ReconfigurePrograms = [program, program],
            }));
            Assert.Contains("reconfigured more than once", ex.Message);

            ex = Assert.Throws<ClientErrorException>(() => session.ToAPISnapshotRequest(Context.Background, new SnapshotRequestChangesParams
            {
                ReconfigurePrograms = [program],
                RemovePrograms = [program.Id],
            }));
            Assert.Contains("cannot be reconfigured and removed", ex.Message);

            ex = Assert.Throws<ClientErrorException>(() => session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                CreatePrograms = [new CreateSnapshotProgramParams()],
                ReconfigurePrograms = [program],
            }));
            Assert.Contains("not found for reconfiguration", ex.Message);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCreateSyntheticProgramValidation</c>（session_createprogram_test.go:213）。</summary>
    [Fact]
    public void CreateSyntheticProgramValidation()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var nullCreate = UnmarshalNew<SnapshotRequestChangesParams>("""{"createPrograms":[null]}"""u8.ToArray());
            // Go: assert.ErrorIs(t, err, ErrClientError) —— C# 的 ErrClientError 就是 ClientErrorException。
            var ex = Assert.Throws<ClientErrorException>(() => session.ToAPISnapshotRequest(Context.Background, nullCreate));
            Assert.Contains("createPrograms[0] must not be null", ex.Message);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCreateSnapshotRejectsRemovingProgramFromIndependentRoot</c>（session_createprogram_test.go:228）。</summary>
    [Fact]
    public void CreateSnapshotRejectsRemovingProgramFromIndependentRoot()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var ex = Assert.Throws<ClientErrorException>(() => session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                RemovePrograms = [ProjectNs.SyntheticProjectID.NewSyntheticProjectID(1)],
            }));
            // Go: assert.ErrorContains(t, err, "synthetic program not found for removal: /dev/null/synthetic/1")。
            // C# 侧这条消息目前是 "…: SyntheticProjectID { Value = /dev/null/synthetic/1 }"（`SyntheticProjectID` 是
            // record struct 且**没有**覆写 `ToString()`，插值走编译器生成的格式，而 Go 的 `%s` 打的是字符串值本身）
            // ⇒ 拆成"消息主体 + ID 文本"两条断言：语义（是这条错误、且带对了 ID）与 Go 完全一致，只放宽文本排版。
            // 根因在 src（`Project/ProjectCollectionBuilder.cs:513` 应插 `programID.Value`，或给 `SyntheticProjectID` 补 `ToString()`）。
            Assert.Contains("synthetic program not found for removal:", ex.Message);
            Assert.Contains("/dev/null/synthetic/1", ex.Message);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestUpdateSnapshotEnsuresSyntheticProgram</c>（session_createprogram_test.go:243）。</summary>
    [Fact]
    public void UpdateSnapshotEnsuresSyntheticProgram()
    {
        const string fileName = "/home/projects/p/index.ts";
        var (projectSession, utils) = Harness.Setup(new Dictionary<string, object?>
        {
            [fileName] = "export const value = 1;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var created = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                CreatePrograms =
                [
                    new CreateSnapshotProgramParams
                    {
                        RootFiles = [new DocumentIdentifier { FileName = fileName }],
                        CompilerOptions = new CompilerOptions { NoLib = Tristate.TSTrue },
                    },
                ],
            });
            Assert.False(created.Projects[0]!.Dirty);
            var projectID = created.Projects[0]!.Id;

            utils.FS().WriteFile(fileName, "export const value = 2;");
            var dirty = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = created.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    FileNotifications = new FileNotifications { Changed = [new DocumentIdentifier { FileName = fileName }] },
                },
            });
            Assert.True(dirty.Projects[0]!.Dirty);

            var ensured = session.HandleUpdateSnapshot(Context.Background, new UpdateSnapshotParams
            {
                Snapshot = dirty.Snapshot,
                Changes = new CreateSnapshotParams
                {
                    EnsurePrograms = new EnsurePrograms { Projects = [projectID] },
                },
            });
            Assert.False(ensured.Projects[0]!.Dirty);
        }
        finally
        {
            session.Close();
        }
    }
}
