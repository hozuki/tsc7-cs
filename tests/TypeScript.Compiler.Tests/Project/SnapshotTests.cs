// ported from: internal/project/snapshot_test.go:1-552
//
// 与 Go 测试的差异（逐条）：
//
// - Go 的 `t.Run("…", …)` 子测试 → 每个子测试一个 `[Fact]`，方法名 = 父测试名 + 子测试名的 PascalCase
//   （`TestSnapshot` + `creates and removes synthetic programs` → `TestSnapshotCreatesAndRemovesSyntheticPrograms`）。
// - Go 的 `assert.Assert(t, x)` / `assert.Check(t, x)` → `Assert.True(x)`；`assert.Equal(t, want, got)` →
//   `Assert.Equal(want, got)`（xunit 是 (expected, actual)）；`assert.NilError(t, err)` → 对 C# 的
//   `(T?, Exception?)` 双返回断言 `Assert.Null(error)`，对 void + 抛异常的 API 则无需断言；
//   `assert.ErrorContains(t, err, "…")` → `Assert.Contains("…", error.Message)`；
//   `assert.DeepEqual(t, []string{…}, got)` → `Assert.Equal(new[] { … }, got)`；
//   Go 比较指针身份（`session.Snapshot() == baseSnapshot`）→ `Assert.Same` / `Assert.NotSame`。
// - Go 的 `defer session.Close()` → `try { … } finally { session.Close(); }`（`Session` 不实现 `IDisposable`，
//   与 Go 的 defer 语义一致；测试文件头说明见 Session.cs 文件头第 12 条）。
// - Go 用 `recover()` 断言 panic → `Assert.Throws<DebugFailureException>(…)`（`Debug.Fail` 抛的异常）。
// - `bundled.WrapFS(vfstest.FromMap(files, false))` → `WrapFS(FromMap(files, false))`；`bundled.LibPath()` → `LibPath()`；
//   文件树 `map[string]any` → `Dictionary<string, object?>`。
// - **不移植 `TestSnapshot` 顶部的 `if !bundled.Embedded { t.Skip(…) }`**：C# 侧 bundled 数据是编译期嵌入的
//   （`Bundled/Embed.Generated.cs`），不存在"没嵌入"的运行形态。
// - **不移植 `BenchmarkSnapshotCloneRefCost`**（Go 的 `testing.B` 基准；本仓测试工程只跑 xunit，没有基准宿主）。
// - `snapshot.fs` / `project.configFilePath` / `project.host` / `session.pendingFileChanges` 等未导出成员 →
//   C# 的 `internal` 成员（`_fs` / `_configFilePath` / `_host` / `_pendingFileChanges` 等），靠
//   `InternalsVisibleTo("TypeScript.Compiler.Tests")` 访问（与 OverlayFsTests.cs 同）。
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using static TypeScript.Bundled.Bundled;
using static TypeScript.Collections.Collections;
using static TypeScript.Project.ProgramUpdateKind;
using static TypeScript.Vfs.OsVfs.Osvfs;
using static TypeScript.Vfs.VfsTest.VfsTest;
using Path = TypeScript.TSPath.Path;
using Range = TypeScript.LsProto.Range;
using Xunit;

namespace TypeScript.Tests;

[Collection("ProjectHubSerial")]
public class ProjectSnapshotTests
{
    /// <summary>Go 的 <c>tspath.Path</c> 零值就是 <c>""</c>，C# 的 <c>default(Path)</c> 里 <c>Value</c> 是 null
    /// （`readonly record struct Path(string Value)` 的主构造器不归一化）⇒ 比较"空路径"时按 Go 的字符串语义比。</summary>
    private static string PathText(Path path) => path.Value ?? "";

    private static Session Setup(Dictionary<string, object?> files)
    {
        var sourceMap = new Dictionary<string, object>();
        foreach (var (key, value) in files)
        {
            sourceMap[key] = value!;
        }

        var fs = WrapFS(FromMap(sourceMap, false /*useCaseSensitiveFileNames*/));
        return new Session(new SessionInit
        {
            BackgroundCtx = Context.Background,
            Options = new SessionOptions
            {
                CurrentDirectory = "/",
                DefaultLibraryPath = LibPath(),
                TypingsLocation = "/home/src/Library/Caches/typescript",
                PositionEncoding = PositionEncodingKind.UTF8,
                WatchEnabled = false,
                LoggingEnabled = false,
            },
            FS = fs,
        });
    }

    [Fact]
    public void TestSnapshotCreatesAndRemovesSyntheticPrograms()
    {
        var session = Setup(new Dictionary<string, object?>
        {
            ["/a.ts"] = "export const a = 1;",
            ["/b.ts"] = "export const b = 1;",
        });
        try
        {
            var ctx = Context.Background;
            var options = new CompilerOptions { NoLib = Tristate.TSTrue };
            var createRequest = new APISnapshotRequest
            {
                CreatePrograms =
                [
                    new APICreateProgramRequest
                    {
                        RootFileNames = ["/a.ts"],
                        CompilerOptions = options,
                    },
                    new APICreateProgramRequest
                    {
                        RootFileNames = ["/b.ts"],
                        CompilerOptions = options,
                    },
                ],
            };
            var (createdSnapshot, error) = session.CloneSnapshot(
                ctx,
                session.Snapshot(),
                new FileChangeSummary(),
                createRequest);
            Assert.Null(error);
            try
            {
                var createdPrograms = createdSnapshot.CreatedPrograms();
                Assert.Equal(2, createdPrograms.Length);
                var firstProject = createdPrograms[0];
                var secondProject = createdPrograms[1];
                Assert.Equal("", PathText(firstProject._configFilePath));
                Assert.Equal("", PathText(secondProject._configFilePath));

                Assert.True(firstProject.ID().TrySynthetic(out var firstProgramID));
                Assert.Equal("/dev/null/synthetic/1", firstProgramID.Value);
                var removeRequest = new APISnapshotRequest { RemovePrograms = NewSetFromItems(firstProgramID) };
                var (removedSnapshot, removeError) = session.CloneSnapshot(
                    ctx,
                    createdSnapshot,
                    new FileChangeSummary(),
                    removeRequest);
                Assert.Null(removeError);
                try
                {
                    Assert.NotNull(firstProject);
                    Assert.NotNull(secondProject);
                    Assert.NotEqual(firstProject.ID(), secondProject.ID());
                    Assert.Equal(["/a.ts"], firstProject.CommandLine!.FileNames());
                    Assert.Equal(["/b.ts"], secondProject.CommandLine!.FileNames());
                    Assert.Null(createdSnapshot.ProjectCollection.InferredProject());
                    Assert.Equal(2, createdSnapshot.ProjectCollection.SyntheticProjects().Length);
                    Assert.Empty(createdSnapshot.ProjectCollection.LanguageServiceProjects());
                    Assert.Empty(createdSnapshot.GetLanguageServiceProjectsContainingFile(new DocumentUri("file:///a.ts")));
                    Assert.Null(createdSnapshot.ProjectCollection.GetDefaultProject(createdSnapshot.ToPath("/a.ts")));
                    Assert.Same(firstProject, createdSnapshot.ProjectCollection.GetProject(firstProject.ID()));

                    var (openedSnapshot, openError) = session.CloneSnapshot(
                        ctx,
                        createdSnapshot,
                        new FileChangeSummary(),
                        new APISnapshotRequest
                        {
                            OpenFiles = new Dictionary<Path, string> { [createdSnapshot.ToPath("/a.ts")] = "/a.ts" },
                        });
                    Assert.Null(openError);
                    try
                    {
                        var inferredProject = openedSnapshot.ProjectCollection.InferredProject();
                        Assert.NotNull(inferredProject);
                        Assert.True(inferredProject.ID().TryInferred(out _));
                        Assert.Equal("", PathText(inferredProject._configFilePath));
                        Assert.Single(openedSnapshot.ProjectCollection.LanguageServiceProjects());
                        Assert.Single(openedSnapshot.GetLanguageServiceProjectsContainingFile(new DocumentUri("file:///a.ts")));
                        Assert.Same(openedSnapshot.ProjectCollection.InferredProject(), openedSnapshot.ProjectCollection.GetDefaultProject(openedSnapshot.ToPath("/a.ts")));
                        Assert.Same(firstProject, openedSnapshot.ProjectCollection.GetProject(firstProject.ID()));

                        Assert.Null(removedSnapshot.ProjectCollection.GetProject(firstProject.ID()));
                        Assert.Same(secondProject, removedSnapshot.ProjectCollection.GetProject(secondProject.ID()));
                        Assert.Single(removedSnapshot.ProjectCollection.SyntheticProjects());
                    }
                    finally
                    {
                        openedSnapshot.Deref();
                    }
                }
                finally
                {
                    removedSnapshot.Deref();
                }
            }
            finally
            {
                createdSnapshot.Deref();
            }
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotFailedApiUpdateIsNotAdopted()
    {
        var session = Setup(new Dictionary<string, object?>
        {
            ["/a.ts"] = "export const a = 1;",
        });
        try
        {
            var baseSnapshot = session.Snapshot();
            var (failedSnapshot, error) = session.CloneSnapshot(
                Context.Background,
                baseSnapshot,
                new FileChangeSummary(),
                new APISnapshotRequest { RemovePrograms = NewSetFromItems(SyntheticProjectID.NewSyntheticProjectID(1)) });
            try
            {
                Assert.Contains("synthetic program not found for removal", error!.Message);
                Assert.Same(baseSnapshot, session.Snapshot());
                Assert.Throws<DebugFailureException>(() =>
                {
                    session.CloneSnapshot(Context.Background, failedSnapshot, new FileChangeSummary(), null);
                });
            }
            finally
            {
                failedSnapshot.Deref();
            }
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotFailedApiUpdatePreservesFlushedHostChanges()
    {
        var session = Setup(new Dictionary<string, object?>
        {
            ["/a.ts"] = "export const a = 1;",
        });
        try
        {
            var baseSnapshot = session.Snapshot();
            lock (session._pendingFileChangesMu)
            {
                session._pendingFileChanges.Add(new FileChange
                {
                    Kind = FileChangeKind.WatchChange,
                    URI = new DocumentUri("file:///a.ts"),
                });
            }

            var error = Assert.ThrowsAny<Exception>(() => session.APIUpdate(
                Context.Background,
                new FileChangeSummary(),
                new APISnapshotRequest { RemovePrograms = NewSetFromItems(SyntheticProjectID.NewSyntheticProjectID(1)) }));

            Assert.Contains("synthetic program not found for removal", error.Message);
            Assert.NotSame(baseSnapshot, session.Snapshot());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotCompilerHostGetsFrozenWithSnapshotsFsOnlyOnce()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/tsconfig.json"] = "{}",
            ["/home/projects/TS/p1/index.ts"] = "console.log('Hello, world!');",
        };
        var session = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/index.ts"), 1, (string)files["/home/projects/TS/p1/index.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, new DocumentUri("untitled:Untitled-1"), 1, "", LanguageKind.TypeScript);
            var snapshotBefore = session.Snapshot();

            session.DidChangeFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/index.ts"), 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    Partial = new TextDocumentContentChangePartial
                    {
                        Text = "\n",
                        Range = new Range
                        {
                            Start = new Position { Line = 0, Character = 24 },
                            End = new Position { Line = 0, Character = 24 },
                        },
                    },
                },
            ]);
            var (_, error) = session.GetLanguageService(Context.Background, new DocumentUri("file:///home/projects/TS/p1/index.ts"));
            Assert.Null(error);
            var snapshotAfter = session.Snapshot();

            // Configured project was updated by a clone
            Assert.Equal(ProgramUpdateKindCloned, snapshotAfter.ProjectCollection.ConfiguredProject(new Path("/home/projects/ts/p1/tsconfig.json"))!.ProgramUpdateKind);
            // Inferred project wasn't updated last snapshot change, so its program update kind is still NewFiles
            Assert.Same(snapshotBefore.ProjectCollection.InferredProject(), snapshotAfter.ProjectCollection.InferredProject());
            Assert.Equal(ProgramUpdateKindNewFiles, snapshotAfter.ProjectCollection.InferredProject()!.ProgramUpdateKind);
            // host for inferred project should not change
            Assert.Same(snapshotBefore._fs, snapshotAfter.ProjectCollection.InferredProject()!._host!._sourceFS._source);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotCachedDiskFilesAreCleanedUp()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/tsconfig.json"] = "{}",
            ["/home/projects/TS/p1/index.ts"] = "import { a } from './a'; console.log(a);",
            ["/home/projects/TS/p1/a.ts"] = "export const a = 1;",
            ["/home/projects/TS/p2/tsconfig.json"] = "{}",
            ["/home/projects/TS/p2/index.ts"] = "import { b } from './b'; console.log(b);",
            ["/home/projects/TS/p2/b.ts"] = "export const b = 2;",
        };
        var session = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/index.ts"), 1, (string)files["/home/projects/TS/p1/index.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p2/index.ts"), 1, (string)files["/home/projects/TS/p2/index.ts"]!, LanguageKind.TypeScript);
            var snapshotBefore = session.Snapshot();

            // a.ts and b.ts are cached
            Assert.True(snapshotBefore._fs._cacheFiles!.ContainsKey(new Path("/home/projects/ts/p1/a.ts")));
            Assert.True(snapshotBefore._fs._cacheFiles!.ContainsKey(new Path("/home/projects/ts/p2/b.ts")));

            // Close p1's only open file
            session.DidCloseFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/index.ts"));
            // Next open file is unrelated to p1, triggers p1 closing and file cache cleanup
            session.DidOpenFile(Context.Background, new DocumentUri("untitled:Untitled-1"), 1, "", LanguageKind.TypeScript);
            var snapshotAfter = session.Snapshot();

            // a.ts is cleaned up, b.ts is still cached
            Assert.False(snapshotAfter._fs._cacheFiles!.ContainsKey(new Path("/home/projects/ts/p1/a.ts")));
            Assert.True(snapshotAfter._fs._cacheFiles!.ContainsKey(new Path("/home/projects/ts/p2/b.ts")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotGetFileReturnsNilForNonExistentFiles()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/tsconfig.json"] = "{}",
            ["/home/projects/TS/p1/index.ts"] = "console.log('Hello, world!');",
        };
        var session = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/index.ts"), 1, (string)files["/home/projects/TS/p1/index.ts"]!, LanguageKind.TypeScript);
            var snapshot = session.Snapshot();

            var handle = snapshot.GetFile("/home/projects/TS/p1/nonexistent.ts");
            Assert.Null(handle);

            // Test that ReadFile returns false for non-existent file
            Assert.False(snapshot.TryReadFile("/home/projects/TS/p1/nonexistent.ts", out _));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotProgramChangeLoadsNodeModulesDependencyAndAutoImportsIncludesIt()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/otherproject/tsconfig.json"] = """
                {
                				"compilerOptions": {
                					"module": "commonjs"
                				}
                			}
                """,
            ["/home/projects/otherproject/index.ts"] = "",
            ["/home/projects/node_modules/foo/package.json"] = """
                {
                				"types": "index.d.ts",
                				"typesVersions": {
                					"*": {
                						"bar/*": ["dist/*"],
                						"exact-match": ["dist/index.d.ts"],
                						"foo/*": ["dist/*"],
                						"*": ["dist/*"]
                					}
                				}
                			}
                """,
            ["/home/projects/node_modules/foo/nope.d.ts"] = "export const nope = 0;",
            ["/home/projects/node_modules/foo/dist/index.d.ts"] = "export const index = 0;",
            ["/home/projects/node_modules/foo/dist/blah.d.ts"] = "export const blah = 0;",
            ["/home/projects/node_modules/foo/dist/foo/onlyInFooFolder.d.ts"] = "export const foo = 0;",
            ["/home/projects/node_modules/foo/dist/subfolder/one.d.ts"] = "export const one = 0;",
        };
        var session = Setup(files);
        try
        {
            var ctx = Context.Background;
            var otherIndexURI = new DocumentUri("file:///home/projects/otherproject/index.ts");

            // Open the file
            session.DidOpenFile(ctx, otherIndexURI, 1, (string)files["/home/projects/otherproject/index.ts"]!, LanguageKind.TypeScript);

            // Insert import statement:
            // This will trigger both a program rebuild which will include the node_modules files,
            // and an auto-import collection which should find the exports from those files.
            session.DidChangeFile(ctx, otherIndexURI, 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    Partial = new TextDocumentContentChangePartial
                    {
                        Text = "import {} from \"foo/foo/subfolder/one\";",
                        Range = new Range
                        {
                            Start = new Position { Line = 0, Character = 0 },
                            End = new Position { Line = 0, Character = 0 },
                        },
                    },
                },
            ]);

            // Now trigger snapshot clone with both program update and auto-imports registry building.
            var (_, error) = session.GetCurrentLanguageServiceWithAutoImports(ctx, otherIndexURI);
            Assert.Null(error);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotFallbackRebuildWithRecomputedParseOptionsIsSafeForLaterClone()
    {
        var testFiles = new Dictionary<string, object?>
        {
            ["/project/node_modules/pkg/index.ts"] = "export const pkg = 0;",
            ["/project/src/other.ts"] = "export const other = 1;",
        };
        var session = Setup(testFiles);
        try
        {
            var pkgURI = new DocumentUri("file:///project/node_modules/pkg/index.ts");
            var otherURI = new DocumentUri("file:///project/src/other.ts");

            session.DidOpenFile(Context.Background, pkgURI, 1, (string)testFiles["/project/node_modules/pkg/index.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, otherURI, 1, (string)testFiles["/project/src/other.ts"]!, LanguageKind.TypeScript);
            var (_, error) = session.GetLanguageService(Context.Background, pkgURI);
            Assert.Null(error);

            session._fs.WriteFile("/project/node_modules/pkg/package.json", """{ "type": "module" }""");
            session.DidChangeFile(Context.Background, pkgURI, 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    WholeDocument = new TextDocumentContentChangeWholeDocument
                    {
                        Text = "import \"./missing\"; export const pkg = 1;",
                    },
                },
            ]);
            (_, error) = session.GetLanguageService(Context.Background, pkgURI);
            Assert.Null(error);

            session.DidChangeFile(Context.Background, otherURI, 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    WholeDocument = new TextDocumentContentChangeWholeDocument
                    {
                        Text = "export const other = 2;",
                    },
                },
            ]);
            (_, error) = session.GetLanguageService(Context.Background, otherURI);
            Assert.Null(error);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotAutoImportSnapshotIsAdoptedWhenSessionSnapshotIsUnchanged()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/tsconfig.json"] = "{}",
            ["/home/projects/TS/p1/index.ts"] = "const value = foo;",
            ["/home/projects/TS/p1/foo.ts"] = "export const foo = 1;",
        };
        var session = Setup(files);
        try
        {
            var ctx = Context.Background;
            var uri = new DocumentUri("file:///home/projects/TS/p1/index.ts");

            session.DidOpenFile(ctx, uri, 1, (string)files["/home/projects/TS/p1/index.ts"]!, LanguageKind.TypeScript);
            var (_, error) = session.GetLanguageService(ctx, uri);
            Assert.Null(error);

            var baseSnapshot = session.Snapshot();
            var preparedSnapshot = session.CloneSnapshotWithAutoImports(ctx, baseSnapshot, uri, null);
            session.TryAdoptSnapshotInBackground(baseSnapshot, preparedSnapshot);
            try
            {
                session.WaitForBackgroundTasks();
                Assert.Same(preparedSnapshot, session.Snapshot());
            }
            finally
            {
                preparedSnapshot.Deref();
            }
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSnapshotNoOpWatchChangeDoesNotRebuildProgram()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/tsconfig.json"] = "{}",
            ["/home/projects/TS/p1/index.ts"] = "import { a } from './a'; console.log(a);",
            ["/home/projects/TS/p1/a.ts"] = "export const a = 1;",
        };
        var session = Setup(files);
        try
        {
            var ctx = Context.Background;
            var uri = new DocumentUri("file:///home/projects/TS/p1/index.ts");
            var configPath = new Path("/home/projects/ts/p1/tsconfig.json");

            session.DidOpenFile(ctx, uri, 1, (string)files["/home/projects/TS/p1/index.ts"]!, LanguageKind.TypeScript);
            var (_, error) = session.GetLanguageService(ctx, uri);
            Assert.Null(error);

            var programBefore = session.Snapshot().ProjectCollection.ConfiguredProject(configPath)!.Program;

            // Send a watch change event for a project file whose content on disk is unchanged.
            // This should not invalidate the program or trigger a recheck.
            lock (session._pendingFileChangesMu)
            {
                session._pendingFileChanges.Add(new FileChange
                {
                    Kind = FileChangeKind.WatchChange,
                    URI = new DocumentUri("file:///home/projects/TS/p1/a.ts"),
                });
            }

            (_, error) = session.GetLanguageService(ctx, uri);
            Assert.Null(error);

            var programAfter = session.Snapshot().ProjectCollection.ConfiguredProject(configPath)!.Program;
            Assert.Same(programBefore, programAfter);

            // A watch change that reflects an actual content change on disk must still
            // rebuild the program.
            session._fs.WriteFile("/home/projects/TS/p1/a.ts", "export const a = 2;");
            lock (session._pendingFileChangesMu)
            {
                session._pendingFileChanges.Add(new FileChange
                {
                    Kind = FileChangeKind.WatchChange,
                    URI = new DocumentUri("file:///home/projects/TS/p1/a.ts"),
                });
            }

            (_, error) = session.GetLanguageService(ctx, uri);
            Assert.Null(error);

            var programChanged = session.Snapshot().ProjectCollection.ConfiguredProject(configPath)!.Program;
            Assert.NotSame(programBefore, programChanged);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestProjectIDNarrowing()
    {
        var configured = new ID("/project/tsconfig.json");
        Assert.True(configured.TryConfigured(out var configuredID));
        Assert.Equal(new ConfiguredProjectID(new Path("/project/tsconfig.json")), configuredID);
        Assert.False(configured.TryInferred(out _));
        Assert.False(configured.TrySynthetic(out _));

        var inferred = InferredProjectID.InferredProjectIDConst.AsID();
        Assert.True(inferred.TryInferred(out var inferredID));
        Assert.Equal(InferredProjectID.InferredProjectIDConst, inferredID);
        Assert.False(inferred.TryConfigured(out _));

        var synthetic = SyntheticProjectID.NewSyntheticProjectID(1).AsID();
        Assert.True(synthetic.TrySynthetic(out var syntheticID));
        Assert.Equal(SyntheticProjectID.NewSyntheticProjectID(1), syntheticID);
        Assert.False(synthetic.TryConfigured(out _));

        Assert.True(new ID("/dev/null/synthetic/01").TrySynthetic(out var canonicalSyntheticID));
        Assert.Equal(SyntheticProjectID.NewSyntheticProjectID(1), canonicalSyntheticID);

        Assert.True(new ID("/dev/null/synthetic/invalid").TryConfigured(out _));

        Assert.False(ConfiguredProjectID.ParseConfiguredProjectID(new Path(ProjectConstants.InferredProjectName), out _));
        Assert.False(ConfiguredProjectID.ParseConfiguredProjectID(new Path(SyntheticProjectID.NewSyntheticProjectID(1).Value), out _));
    }
}
