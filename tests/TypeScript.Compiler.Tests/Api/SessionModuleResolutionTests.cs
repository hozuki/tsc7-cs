// ported from: internal/api/session_module_resolution_test.go:1-312
//
// 与 Go 测试的差异（逐条）：
// 1) `failingModuleResolutionConn`（Go 的 `ipc.Conn` 实现，`Call` 恒返回 `errors.New("callback error")`）→ C# 实现
//    `TypeScript.Ipc.Conn`：`Call` **抛** `Exception("callback error")`（本仓 `(T, error)` → 返回值 + 抛异常的映射；
//    CallbackModuleResolver 会把它包成 `InvalidOperationException("resolveModuleName callback failed: …")`，
//    所以 `assert.ErrorContains(t, err, "callback error")` 的断言仍然成立）。`conn.calls` → `Calls` 字段。
// 2) 未导出类型/字段的直接使用：`moduleResolverFactory` → `ModuleResolverFactory`、`moduleResolverRegistration` →
//    `ModuleResolverRegistration`、`session.programResolutionContexts` → `session._programResolutionContexts`
//    （均为 `internal`，靠 `InternalsVisibleTo("TypeScript.Compiler.Tests")`，先例 CheckerPoolTests.cs 文件头第 4 条）。
//    `moduleResolverFactory{id: 1, resolveModuleNameCallback: …}` → 相应的字段初始化。
// 3) `core.ModuleKindNodeNext`/`core.ModuleResolutionKindNodeNext` → `ModuleKind.NodeNext`/`ModuleResolutionKind.NodeNext`；
//    `core.TSTrue` → `Tristate.TSTrue`；`core.ModuleKindESNext` → `ModuleKind.ESNext`；
//    `core.ResolutionModeESM` → `Core.ResolutionModeESM`（`TypeScript.Core.Core` 的常量，`using static TypeScript.Core.Core;`
//    后是裸名）。
// 4) `*core.ModuleKind`（`ResolutionMode *ResolutionMode`，`ResolutionMode = core.ModuleKind`）→ C# `ModuleKind?`；
//    Go 的 `resolutionMode := ResolutionMode(mode)` 在 C# 直接赋 `mode`（隐式可空转换）。
// 5) `module.ResolverOptions{Host: session, CompilerOptions: core.EmptyCompilerOptions}` →
//    `new TypeScript.Module.ResolverOptions { Host = session, CompilerOptions = EmptyCompilerOptions }`（`EmptyCompilerOptions` 是静态类 `Core` 的成员，靠 `using static TypeScript.Core.Core;` 取裸名）。
// 6) `assert.Assert(t, len(result.Trace) > 0)` → `Assert.True(result.Trace is { Length: > 0 })`；`len(result.Trace) == 0` →
//    `Assert.True(result.Trace is null or { Length: 0 })`（Go 的 nil 与空切片 `len` 都是 0）。
// 7) `assert.DeepEqual(t, fileNames, []string{provided, root})` → `Assert.Equal(new[] { provided, root }, fileNames)`。
// 8) `session.handleRelease` 等错误路径：`assert.ErrorContains` → `Assert.Throws<…>` + `Assert.Contains`。
// 9) `(*response.Operation.CreatedPrograms)[0].AsID()` → `response.Operation!.CreatedPrograms![0].AsID()`
//    （`SyntheticProjectID.AsID()` 即 Go 的 `AsID()`）。
// 10) TestModuleResolutionCallbackErrorRejectsLanguageServerUpdate 里 Go 直接写 `session.conn = &failingModuleResolutionConn{}`
//     → `session._conn = new FailingModuleResolutionConn()`（`_conn` 是 `internal Ipc.Conn?` 字段）。
//     错误类型：Go 只断言消息，C# 侧 `handleGetCurrentLanguageServerSnapshot` 把 APIUpdate 的失败包成
//     `ClientErrorException("failed to update language server snapshot: …")`，故断言该类型 + 消息包含 "callback error"。
// 11) Go 的 `defer projectSession.Close()`（`Setup` 起的项目会话）不移植：本仓测试的既有约定是只关 API 会话
//     （先例 Project/ConfigFileChangesTests.cs），对可观察行为无影响（见 SessionCreateProgramTests.cs 文件头第 13 条）。
// 12) `projecttestutil.Setup(files)` → `Harness.Setup(files)`：别名取 `Harness` 的理由见
//     SessionCreateProgramTests.cs 文件头第 1 条（`ProjectTestUtil` 在 `TypeScript.Tests` 下是命名空间名）。
using System;
using System.Collections.Generic;
using TypeScript.Api;
using TypeScript.Core;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using ApiSession = TypeScript.Api.Session;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;
using static TypeScript.Core.Core;

namespace TypeScript.Tests;

public class SessionModuleResolutionTests
{
    /// <summary>Go: <c>TestModuleResolverUsesSnapshotFileSystem</c>（session_module_resolution_test.go:32）。</summary>
    [Fact]
    public void ModuleResolverUsesSnapshotFileSystem()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/home/projects/p/node_modules/pkg/package.json"] =
                """{"name":"pkg","version":"1.0.0","exports":{".":{"types":"./index.d.ts","default":"./index.js"}}}""",
            ["/home/projects/p/node_modules/pkg/index.d.ts"] = "export declare const value: string;",
            ["/home/projects/p/node_modules/pkg/index.js"] = "exports.value = \"value\";",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var snapshot = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams());
            var resolver = session.HandleCreateModuleResolver(new CreateModuleResolverParams
            {
                CompilerOptions = new CompilerOptions
                {
                    Module = ModuleKind.NodeNext,
                    ModuleResolution = ModuleResolutionKind.NodeNext,
                    TraceResolution = Tristate.TSTrue,
                },
            });
            var result = session.HandleResolveModuleName(Context.Background, new ResolveModuleNameParams
            {
                Snapshot = snapshot.Snapshot,
                Resolver = resolver,
                ModuleName = "pkg",
                ContainingDirectory = new DocumentIdentifier { FileName = "/home/projects/p/src" },
            });

            Assert.Equal("/home/projects/p/node_modules/pkg/index.d.ts", result.ResolvedModule!.ResolvedFileName);
            Assert.Equal("pkg", result.ResolvedModule!.PackageId!.Name);
            Assert.True(result.Trace is { Length: > 0 });
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestStaticModuleResolutionSpecificityAndLifetime</c>（session_module_resolution_test.go:66）。</summary>
    [Fact]
    public void StaticModuleResolutionSpecificityAndLifetime()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/home/projects/p/global.d.ts"] = "export declare const value: \"global\";",
            ["/home/projects/p/mode.d.ts"] = "export declare const value: \"mode\";",
            ["/home/projects/p/dir.d.ts"] = "export declare const value: \"dir\";",
            ["/home/projects/p/exact.d.ts"] = "export declare const value: \"exact\";",
            ["/home/projects/p/default.d.ts"] = "export declare const value: \"default\";",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var snapshot = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams());
            var spec = new ModuleResolutionSpec
            {
                Fallback = ModuleResolutionFallback.Unresolved,
                Entries =
                [
                    StaticResolutionEntry("pkg", "", null, "/home/projects/p/global.d.ts"),
                    StaticResolutionEntry("pkg", "", ModuleKind.ESNext, "/home/projects/p/mode.d.ts"),
                    StaticResolutionEntry("pkg", "/home/projects/p/src", null, "/home/projects/p/dir.d.ts"),
                    StaticResolutionEntry("pkg", "/home/projects/p/src", ModuleKind.ESNext, "/home/projects/p/exact.d.ts"),
                ],
            };
            var resolverID = session.HandleCreateModuleResolver(new CreateModuleResolverParams
            {
                CompilerOptions = new CompilerOptions { ModuleResolution = ModuleResolutionKind.NodeNext },
                ModuleResolutions = spec,
            });

            void AssertResolution(string directory, ModuleKind mode, string expected)
            {
                var result = session.HandleResolveModuleName(Context.Background, new ResolveModuleNameParams
                {
                    Snapshot = snapshot.Snapshot,
                    Resolver = resolverID,
                    ModuleName = "pkg",
                    ContainingDirectory = new DocumentIdentifier { FileName = directory },
                    ResolutionMode = mode,
                });
                Assert.Equal(expected, result.ResolvedModule!.ResolvedFileName);
                Assert.True(result.Trace is null or { Length: 0 });
            }

            AssertResolution("/home/projects/p/src", ModuleKind.ESNext, "/home/projects/p/exact.d.ts");
            AssertResolution("/home/projects/p/src", ModuleKind.CommonJS, "/home/projects/p/dir.d.ts");
            AssertResolution("/home/projects/p/other", ModuleKind.ESNext, "/home/projects/p/mode.d.ts");
            AssertResolution("/home/projects/p/other", ModuleKind.CommonJS, "/home/projects/p/global.d.ts");

            var unresolved = session.HandleResolveModuleName(Context.Background, new ResolveModuleNameParams
            {
                Snapshot = snapshot.Snapshot,
                Resolver = resolverID,
                ModuleName = "other",
                ContainingDirectory = new DocumentIdentifier { FileName = "/home/projects/p/src" },
            });
            Assert.Null(unresolved.ResolvedModule);

            AssertResolution("/home/projects/p/src", ModuleKind.ESNext, "/home/projects/p/exact.d.ts");
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestCreateProgramUsesStaticModuleResolutions</c>（session_module_resolution_test.go:129）。</summary>
    [Fact]
    public void CreateProgramUsesStaticModuleResolutions()
    {
        const string root = "/home/projects/p/src/index.ts";
        const string provided = "/home/projects/p/provided.d.ts";
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            [root] = "import { value } from \"pkg\"; export { value };",
            [provided] = "export declare const value: string;",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var resolver = session.HandleCreateModuleResolver(new CreateModuleResolverParams
            {
                CompilerOptions = new CompilerOptions
                {
                    NoLib = Tristate.TSTrue,
                    Module = ModuleKind.NodeNext,
                    ModuleResolution = ModuleResolutionKind.NodeNext,
                },
                ModuleResolutions = new ModuleResolutionSpec
                {
                    Fallback = ModuleResolutionFallback.Unresolved,
                    Entries = [StaticResolutionEntry("pkg", "", null, provided)],
                },
            });

            var response = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams
            {
                CreatePrograms =
                [
                    new CreateSnapshotProgramParams
                    {
                        RootFiles = [new DocumentIdentifier { FileName = root }],
                        CompilerOptions = new CompilerOptions
                        {
                            NoLib = Tristate.TSTrue,
                            Module = ModuleKind.NodeNext,
                            ModuleResolution = ModuleResolutionKind.NodeNext,
                        },
                        Options = new CreateProgramOptions { ModuleResolver = resolver },
                    },
                ],
            });
            var projectID = response.Operation!.CreatedPrograms![0].AsID();
            var fileNames = session.HandleGetSourceFileNames(Context.Background, new GetSourceFileNamesParams
            {
                Snapshot = response.Snapshot,
                Project = projectID,
            });
            Assert.Equal(new[] { provided, root }, fileNames);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestStaticModuleResolutionPreservesStaticIdentity</c>（session_module_resolution_test.go:181）。</summary>
    [Fact]
    public void StaticModuleResolutionPreservesStaticIdentity()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var snapshot = session.HandleCreateSnapshot(Context.Background, new CreateSnapshotParams());
            var resolver = session.HandleCreateModuleResolver(new CreateModuleResolverParams
            {
                CompilerOptions = new CompilerOptions { ModuleResolution = ModuleResolutionKind.NodeNext },
                ModuleResolutions = new ModuleResolutionSpec
                {
                    Fallback = ModuleResolutionFallback.Unresolved,
                    Entries =
                    [
                        new ModuleResolutionEntry
                        {
                            ModuleName = "pkg",
                            Result = new StaticModuleResolution
                            {
                                ResolvedFileName = new DocumentIdentifier { FileName = "/store/pkg/index.d.ts" },
                                OriginalPath = new DocumentIdentifier { FileName = "/node_modules/pkg/index.d.ts" },
                                PackageID = new PackageId
                                {
                                    Name = "pkg",
                                    SubModuleName = "",
                                    Version = "1.2.3",
                                },
                            },
                        },
                    ],
                },
            });
            var result = session.HandleResolveModuleName(Context.Background, new ResolveModuleNameParams
            {
                Snapshot = snapshot.Snapshot,
                Resolver = resolver,
                ModuleName = "pkg",
                ContainingDirectory = new DocumentIdentifier { FileName = "/src" },
            });

            Assert.Equal("/node_modules/pkg/index.d.ts", result.ResolvedModule!.OriginalPath);
            Assert.Equal("pkg", result.ResolvedModule!.PackageId!.Name);
            Assert.Equal("1.2.3", result.ResolvedModule!.PackageId!.Version);
            Assert.True(result.ResolvedModule!.IsExternalLibraryImport);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestModuleResolutionCallbackErrorsAreReturned</c>（session_module_resolution_test.go:225）。</summary>
    [Fact]
    public void ModuleResolutionCallbackErrorsAreReturned()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var conn = new FailingModuleResolutionConn();
            var registration = new ModuleResolverRegistration
            {
                Id = new ModuleResolverID(1),
                ResolveModuleNameCallback = "resolveModuleName/1",
            };
            var factory = new ModuleResolverFactory
            {
                Registration = registration,
                Session = session,
                Conn = conn,
                Ctx = Context.Background,
                CurrentDirectory = "/",
            };
            var (provider, cleanup) = factory.NewResolver(new TypeScript.Module.ResolverOptions
            {
                Host = session,
                CompilerOptions = EmptyCompilerOptions,
            });
            for (var i = 0; i < 2; i++)
            {
                var ex = Assert.Throws<InvalidOperationException>(
                    () => provider.ResolveModuleNameFromDirectory("pkg", "/src", ResolutionModeESM));
                Assert.Contains("callback error", ex.Message);
            }

            Assert.Equal(2, conn.Calls);
            Assert.Single(session._programResolutionContexts);
            cleanup();
            Assert.Empty(session._programResolutionContexts);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestModuleResolutionCallbackErrorRejectsLanguageServerUpdate</c>（session_module_resolution_test.go:258）。</summary>
    [Fact]
    public void ModuleResolutionCallbackErrorRejectsLanguageServerUpdate()
    {
        var (projectSession, _) = Harness.Setup(new Dictionary<string, object?>
        {
            ["/src/index.ts"] = "import \"pkg\";",
        });
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            session._conn = new FailingModuleResolutionConn();
            var resolver = session.HandleCreateModuleResolver(new CreateModuleResolverParams
            {
                CompilerOptions = new CompilerOptions
                {
                    NoLib = Tristate.TSTrue,
                    Module = ModuleKind.NodeNext,
                    ModuleResolution = ModuleResolutionKind.NodeNext,
                },
                ResolveModuleNameCallback = "resolveModuleName/1",
            });
            var baseSnapshot = projectSession.Snapshot();

            var ex = Assert.Throws<ClientErrorException>(() => session.HandleGetCurrentLanguageServerSnapshot(
                Context.Background,
                new GetCurrentLanguageServerSnapshotParams
                {
                    Changes = new LanguageServerSnapshotChanges
                    {
                        CreatePrograms =
                        [
                            new CreateSnapshotProgramParams
                            {
                                RootFiles = [new DocumentIdentifier { FileName = "/src/index.ts" }],
                                CompilerOptions = new CompilerOptions
                                {
                                    NoLib = Tristate.TSTrue,
                                    Module = ModuleKind.NodeNext,
                                    ModuleResolution = ModuleResolutionKind.NodeNext,
                                },
                                Options = new CreateProgramOptions { ModuleResolver = resolver },
                            },
                        ],
                    },
                }));
            Assert.Contains("callback error", ex.Message);
            Assert.Same(baseSnapshot, projectSession.Snapshot());
            Assert.Empty(projectSession.Snapshot().ProjectCollection.SyntheticProjects());
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>func staticResolutionEntry(moduleName, directory string, mode *core.ModuleKind, fileName string) *ModuleResolutionEntry</c>（session_module_resolution_test.go:297）。</summary>
    private static ModuleResolutionEntry StaticResolutionEntry(string moduleName, string directory, ModuleKind? mode, string fileName)
    {
        var entry = new ModuleResolutionEntry
        {
            ModuleName = moduleName,
            Result = new StaticModuleResolution
            {
                ResolvedFileName = new DocumentIdentifier { FileName = fileName },
            },
        };
        if (mode is not null)
        {
            entry.ResolutionMode = mode.Value;
        }

        if (directory != "")
        {
            entry.ContainingDirectory = new DocumentIdentifier { FileName = directory };
        }

        return entry;
    }

    /// <summary>Go: <c>type failingModuleResolutionConn struct</c>（session_module_resolution_test.go:15）。</summary>
    private sealed class FailingModuleResolutionConn : TypeScript.Ipc.Conn
    {
        internal int Calls;

        public void Run(Context ctx)
        {
        }

        public byte[]? Call(Context ctx, string method, object? @params)
        {
            Calls++;
            throw new Exception("callback error");
        }

        public void Notify(Context ctx, string method, object? @params)
        {
        }
    }
}
