// ported from: internal/project/session_test.go:23-604（TestSession 的前六组）
//
// 与 Go 测试的差异（逐条）：
//
// - `t.Run("…")` 子测试 → 一个 `[Fact]`，方法名 = `TestSession` + 各组/子测试名的 PascalCase 拼接
//   （`DidOpenFile` → `TestSessionDidOpenFile…`）。Go 的 `t.Parallel()` 不移植（xunit 默认同一 collection
//   内串行；本组测试各自建会话，互不共享状态）。
// - `projecttestutil.Setup(files)` → `Setup(files)`，走 `using static TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;`
//   （`ProjectTestUtil` 既是命名空间又是类：裸写 `ProjectTestUtil.Setup` 会命中命名空间 → CS0234）。
// - `assert.NilError(t, err)` → 对 `Session.GetLanguageService` 的 `(T?, Exception?)` 双返回断言
//   `Assert.Null(error)`；`assert.Check(t, x)` → `Assert.True(x)`；`assert.Equal(t, want, got)` →
//   `Assert.Equal(want, got)`；Go 的指针身份比较（`programAfter != programBefore`、`x1 != x2`）→
//   `Assert.NotSame`（相同则 `Assert.Same`）。
// - `utils.FS().WriteFile(...)` / `Remove(...)` 在 C# 是 void（失败抛异常）⇒ Go 的 `assert.NilError(t, err)` 消失。
// - `maps.Clone(defaultFiles)` → `new Dictionary<string, object?>(defaultFiles)`；`delete(files, k)` → `files.Remove(k)`。
// - `ls.GetProgram()` 在 C# 是可空的（`*compiler.Program` → `Program?`）⇒ 断言后取 `!`。
// - Go 的 `defer session.Close()` → `try { … } finally { session.Close(); }`（`Session` 不实现 `IDisposable`）。
// - **未移植的部分**：`DidChangeWatchedFiles` 组（Go session_test.go:605-1481，19 个子测试）与
//   `refreshes code lenses…`/`sets locale…`/`locale change…`/`adds locale…`（1481-1738）尚未移完
//   （见交付报告"未覆盖清单"）。
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.Tests.ProjectTestUtil;
using static TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;
using Path = TypeScript.TSPath.Path;
using Range = TypeScript.LsProto.Range;
using Xunit;

namespace TypeScript.Tests;

/// <summary>Session/Snapshot 用例会建真实会话并驱动全局缓存（bundled 资源、解析缓存、auto-import 注册表）：
/// 实测两个测试类并发执行时会互相干扰（每轮的失败集合不同）。Go 侧能在同一进程里 `t.Parallel()`
/// 成立是因为每个 `Setup` 都建独立 FS，但 C# 侧仍有共享的静态状态，所以把这些重用例收进一个
/// **禁并行**的 collection，保证确定性（xunit 默认不同 collection 之间并行）。</summary>
[CollectionDefinition("ProjectHubSerial", DisableParallelization = true)]
public class ProjectHubSerialCollection
{
}

[Collection("ProjectHubSerial")]
public class ProjectSessionTests
{
    private static Dictionary<string, object?> DefaultFiles() => new()
    {
        ["/home/projects/TS/p1/tsconfig.json"] = """
            {
            			"compilerOptions": {
            				"noLib": true,
            				"module": "nodenext",
            				"strict": true
            			},
            			"include": ["src"]
            		}
            """,
        ["/home/projects/TS/p1/src/index.ts"] = """import { x } from "./x";""",
        ["/home/projects/TS/p1/src/x.ts"] = "export const x = 1;",
        ["/home/projects/TS/p1/config.ts"] = "let x = 1, y = 2;",
    };

    private static Ls.LanguageService Open(Session session, DocumentUri uri)
    {
        var (languageService, error) = session.GetLanguageService(Context.Background, uri);
        Assert.Null(error);
        return languageService!;
    }

    [Fact]
    public void TestSessionDidOpenFileCreateConfiguredProject()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            var snapshot = session.Snapshot();
            Assert.Empty(snapshot.ProjectCollection.Projects());

            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());

            var configuredProject = snapshot.ProjectCollection.ConfiguredProject(new Path("/home/projects/ts/p1/tsconfig.json"));
            Assert.NotNull(configuredProject);

            // Get language service to access the program
            var ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"));
            var program = ls.GetProgram();
            Assert.NotNull(program!.GetSourceFile("/home/projects/TS/p1/src/x.ts"));
            Assert.Equal("export const x = 1;", program.GetSourceFile("/home/projects/TS/p1/src/x.ts")!.Text());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidOpenFileCreateInferredProject()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/config.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/config.ts"]!, LanguageKind.TypeScript);

            // Find tsconfig, load, notice config.ts is not included, create inferred project
            var snapshot = session.Snapshot();
            Assert.Equal(2, snapshot.ProjectCollection.Projects().Length);

            // Should have both configured project (for tsconfig.json) and inferred project
            var configuredProject = snapshot.ProjectCollection.ConfiguredProject(new Path("/home/projects/ts/p1/tsconfig.json"));
            var inferredProject = snapshot.ProjectCollection.InferredProject();
            Assert.NotNull(configuredProject);
            Assert.NotNull(inferredProject);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidOpenFileInferredProjectForInMemoryFiles()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/config.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/config.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, new DocumentUri("untitled:Untitled-1"), 1, "x", LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, new DocumentUri("untitled:Untitled-2"), 1, "y", LanguageKind.TypeScript);

            var snapshot = session.Snapshot();

            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidOpenFileInferredProjectJsFile()
    {
        var jsFiles = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/index.js"] = """import { x } from "./x";""",
        };
        var (session, _) = Setup(jsFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/index.js"), 1, (string)jsFiles["/home/projects/TS/p1/index.js"]!, LanguageKind.JavaScript);

            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());

            var ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/index.js"));
            var program = ls.GetProgram();
            Assert.NotNull(program!.GetSourceFile("/home/projects/TS/p1/index.js"));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidOpenFileInferredProjectExtensionlessFile()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/script"] = "const x = 1;",
        };
        var (session, _) = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/script"), 1, (string)files["/home/projects/TS/p1/script"]!, new LanguageKind("plaintext"));

            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            var ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/script"));
            var program = ls.GetProgram();
            var file = program!.GetSourceFile("/home/projects/TS/p1/script");
            Assert.NotNull(file);
            Assert.Equal(ScriptKind.TS, file.ScriptKind);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionWatchChangeAndDidOpenInSameBatchRebuildsProgram()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/tsconfig.json"] = """
                {
                			"compilerOptions": {
                				"noLib": true,
                				"strict": true
                			}
                		}
                """,
            ["/home/projects/TS/p1/src/a.ts"] = "export const a = 1;\n",
            ["/home/projects/TS/p1/src/b.ts"] = "export const b = 1;\n",
        };
        var (session, utils) = Setup(files);
        try
        {
            var oldContent = (string)files["/home/projects/TS/p1/src/a.ts"]!;

            // Open b.ts to create the project; a.ts is included via tsconfig.
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/b.ts"), 1, (string)files["/home/projects/TS/p1/src/b.ts"]!, LanguageKind.TypeScript);

            // Verify a.ts is in the program with the original content.
            var ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/b.ts"));
            Assert.Equal(oldContent, ls.GetProgram()!.GetSourceFile("/home/projects/TS/p1/src/a.ts")!.Text());

            // Modify a.ts on disk (simulate a build tool or git checkout).
            var newContent = "export const a = 2;\nexport const extra = true;\n";
            utils.FS().WriteFile("/home/projects/TS/p1/src/a.ts", newContent);

            // Queue a watch event for the disk change (not flushed yet).
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Type = FileChangeType.Changed, Uri = new DocumentUri("file:///home/projects/TS/p1/src/a.ts") },
            ]);

            // Open a.ts in the editor—flushes both watch event and didOpen together.
            // Before the fix, processChanges would discard the watch event,
            // leaving the project with a stale SourceFile and a mismatched line map.
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/a.ts"), 1, newContent, LanguageKind.TypeScript);

            // The program's SourceFile must reflect the overlay (new) content.
            ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/a.ts"));
            Assert.Equal(newContent, ls.GetProgram()!.GetSourceFile("/home/projects/TS/p1/src/a.ts")!.Text());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidChangeFileUpdateFileAndProgram()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/src/x.ts"]!, LanguageKind.TypeScript);

            var programBefore = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/x.ts")).GetProgram();

            session.DidChangeFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    Partial = new TextDocumentContentChangePartial
                    {
                        Range = new Range
                        {
                            Start = new Position { Line = 0, Character = 17 },
                            End = new Position { Line = 0, Character = 18 },
                        },
                        Text = "2",
                    },
                },
            ]);

            var programAfter = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/x.ts")).GetProgram();

            // Program should change due to the file content change
            Assert.NotSame(programBefore, programAfter);
            Assert.Equal("export const x = 2;", programAfter!.GetSourceFile("/home/projects/TS/p1/src/x.ts")!.Text());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidChangeFileUpdateUntitledFile()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("untitled:Untitled-1"), 1, "let x = 1;", LanguageKind.TypeScript);

            var programBefore = Open(session, new DocumentUri("untitled:Untitled-1")).GetProgram();
            var untitledFileName = new DocumentUri("untitled:Untitled-1").FileName();
            Assert.Equal("let x = 1;", programBefore!.GetSourceFile(untitledFileName)!.Text());

            session.DidChangeFile(Context.Background, new DocumentUri("untitled:Untitled-1"), 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    Partial = new TextDocumentContentChangePartial
                    {
                        Range = new Range
                        {
                            Start = new Position { Line = 0, Character = 8 },
                            End = new Position { Line = 0, Character = 9 },
                        },
                        Text = "2",
                    },
                },
            ]);

            var programAfter = Open(session, new DocumentUri("untitled:Untitled-1")).GetProgram();

            Assert.NotSame(programBefore, programAfter);
            Assert.Equal("let x = 2;", programAfter!.GetSourceFile(untitledFileName)!.Text());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidChangeFileUnchangedSourceFilesAreReused()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/src/x.ts"]!, LanguageKind.TypeScript);

            var programBefore = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/x.ts")).GetProgram();
            var indexFileBefore = programBefore!.GetSourceFile("/home/projects/TS/p1/src/index.ts");

            session.DidChangeFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    Partial = new TextDocumentContentChangePartial
                    {
                        Range = new Range
                        {
                            Start = new Position { Line = 0, Character = 0 },
                            End = new Position { Line = 0, Character = 0 },
                        },
                        Text = ";",
                    },
                },
            ]);

            var programAfter = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/x.ts")).GetProgram();

            // Unchanged file should be reused
            Assert.Same(indexFileBefore, programAfter!.GetSourceFile("/home/projects/TS/p1/src/index.ts"));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidChangeFileChangeCanPullInNewFiles()
    {
        var defaultFiles = DefaultFiles();
        var files = new Dictionary<string, object?>(defaultFiles)
        {
            ["/home/projects/TS/p1/y.ts"] = "export const y = 2;",
        };
        var (session, _) = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            // Verify y.ts is not initially in the program
            var programBefore = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts")).GetProgram();
            Assert.Null(programBefore!.GetSourceFile("/home/projects/TS/p1/y.ts"));

            session.DidChangeFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    Partial = new TextDocumentContentChangePartial
                    {
                        Range = new Range
                        {
                            Start = new Position { Line = 0, Character = 0 },
                            End = new Position { Line = 0, Character = 0 },
                        },
                        Text = "import { y } from \"../y\";\n",
                    },
                },
            ]);

            var programAfter = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts")).GetProgram();

            // y.ts should now be included in the program
            Assert.NotNull(programAfter!.GetSourceFile("/home/projects/TS/p1/y.ts"));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidChangeFileSingleFileChangeFollowedByConfigChangeReloadsProgram()
    {
        var defaultFiles = DefaultFiles();
        var files = new Dictionary<string, object?>(defaultFiles)
        {
            ["/home/projects/TS/p1/tsconfig.json"] = """
                {
                			"compilerOptions": {
                				"noLib": true,
                				"module": "nodenext",
                				"strict": true
                			},
                			"include": ["src/index.ts"]
                		}
                """,
        };
        var (session, utils) = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            var programBefore = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts")).GetProgram();
            Assert.Equal(2, programBefore!.GetSourceFiles().Length);

            session.DidChangeFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    Partial = new TextDocumentContentChangePartial
                    {
                        Range = new Range
                        {
                            Start = new Position { Line = 0, Character = 0 },
                            End = new Position { Line = 0, Character = 0 },
                        },
                        Text = "\n",
                    },
                },
            ]);

            utils.FS().WriteFile("/home/projects/TS/p1/tsconfig.json", """
                {
                			"compilerOptions": {
                				"noLib": true,
                				"module": "nodenext",
                				"strict": true
                			},
                			"include": ["./**/*"]
                		}
                """);

            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Type = FileChangeType.Changed, Uri = new DocumentUri("file:///home/projects/TS/p1/tsconfig.json") },
            ]);

            var programAfter = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts")).GetProgram();
            Assert.Equal(3, programAfter!.GetSourceFiles().Length);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidCloseFileConfiguredProjectsDeleteAFileCloseItRecreateIt()
    {
        var defaultFiles = DefaultFiles();
        var files = new Dictionary<string, object?>(defaultFiles);
        var (session, utils) = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 1, (string)files["/home/projects/TS/p1/src/x.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            utils.FS().Remove("/home/projects/TS/p1/src/x.ts");

            session.DidCloseFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"));
            var ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"));
            Assert.Null(ls.GetProgram()!.GetSourceFile("/home/projects/TS/p1/src/x.ts"));

            utils.FS().WriteFile("/home/projects/TS/p1/src/x.ts", "");

            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 1, "", LanguageKind.TypeScript);

            ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"));
            Assert.NotNull(ls.GetProgram()!.GetSourceFile("/home/projects/TS/p1/src/x.ts"));
            Assert.Equal("", ls.GetProgram()!.GetSourceFile("/home/projects/TS/p1/src/x.ts")!.Text());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidCloseFileInferredProjectsDeleteAFileCloseItRecreateIt()
    {
        var defaultFiles = DefaultFiles();
        var files = new Dictionary<string, object?>(defaultFiles);
        files.Remove("/home/projects/TS/p1/tsconfig.json");
        var (session, utils) = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 1, (string)files["/home/projects/TS/p1/src/x.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            utils.FS().Remove("/home/projects/TS/p1/src/x.ts");

            session.DidCloseFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"));

            var ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"));
            Assert.Null(ls.GetProgram()!.GetSourceFile("/home/projects/TS/p1/src/x.ts"));

            utils.FS().WriteFile("/home/projects/TS/p1/src/x.ts", "");

            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 1, "", LanguageKind.TypeScript);

            ls = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"));
            Assert.NotNull(ls.GetProgram()!.GetSourceFile("/home/projects/TS/p1/src/x.ts"));
            Assert.Equal("", ls.GetProgram()!.GetSourceFile("/home/projects/TS/p1/src/x.ts")!.Text());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidCloseFileInferredProjectsCloseUntitledFile()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("untitled:Untitled-1"), 1, "let x = 1;", LanguageKind.TypeScript);
            session.DidCloseFile(Context.Background, new DocumentUri("untitled:Untitled-1"));
            session.DidOpenFile(Context.Background, new DocumentUri("untitled:Untitled-2"), 1, "", LanguageKind.TypeScript);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidSaveFileSaveEventFirst()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            var snapshot = session.Snapshot();
            Assert.Equal((ulong)1, snapshot.ID());

            session.DidSaveFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"));
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Type = FileChangeType.Changed, Uri = new DocumentUri("file:///home/projects/TS/p1/src/index.ts") },
            ]);

            session.WaitForBackgroundTasks();
            snapshot = session.Snapshot();
            // We didn't need a snapshot change, but the session overlays should be updated.
            Assert.Equal((ulong)1, snapshot.ID());

            // Open another file to force a snapshot update so we can see the changes.
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/src/x.ts"]!, LanguageKind.TypeScript);
            snapshot = session.Snapshot();
            Assert.True(snapshot.GetFile("/home/projects/TS/p1/src/index.ts")!.MatchesDiskText());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionDidSaveFileWatchEventFirst()
    {
        var defaultFiles = DefaultFiles();
        var (session, _) = Setup(defaultFiles);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            var snapshot = session.Snapshot();
            Assert.Equal((ulong)1, snapshot.ID());

            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Type = FileChangeType.Changed, Uri = new DocumentUri("file:///home/projects/TS/p1/src/index.ts") },
            ]);
            session.DidSaveFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"));

            session.WaitForBackgroundTasks();
            snapshot = session.Snapshot();
            // We didn't need a snapshot change, but the session overlays should be updated.
            Assert.Equal((ulong)1, snapshot.ID());

            // Open another file to force a snapshot update so we can see the changes.
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/x.ts"), 1, (string)defaultFiles["/home/projects/TS/p1/src/x.ts"]!, LanguageKind.TypeScript);
            snapshot = session.Snapshot();
            Assert.True(snapshot.GetFile("/home/projects/TS/p1/src/index.ts")!.MatchesDiskText());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionSourceFileSharingProjectsWithSimilarOptionsShareSourceFiles()
    {
        var defaultFiles = DefaultFiles();
        var files = new Dictionary<string, object?>(defaultFiles)
        {
            ["/home/projects/TS/p2/tsconfig.json"] = """
                {
                			"compilerOptions": {
                				"noLib": true,
                				"module": "nodenext",
                				"strict": true,
                				"noCheck": true
                			}
                		}
                """,
            ["/home/projects/TS/p2/src/index.ts"] = """import { x } from "../../p1/src/x";""",
        };
        var (session, _) = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p2/src/index.ts"), 1, (string)files["/home/projects/TS/p2/src/index.ts"]!, LanguageKind.TypeScript);

            var snapshot = session.Snapshot();
            Assert.Equal(2, snapshot.ProjectCollection.Projects().Length);

            var program1 = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts")).GetProgram();
            var program2 = Open(session, new DocumentUri("file:///home/projects/TS/p2/src/index.ts")).GetProgram();

            Assert.Same(
                program1!.GetSourceFile("/home/projects/TS/p1/src/x.ts"),
                program2!.GetSourceFile("/home/projects/TS/p1/src/x.ts"));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void TestSessionSourceFileSharingProjectsWithDifferentOptionsDoNotShareSourceFiles()
    {
        var defaultFiles = DefaultFiles();
        var files = new Dictionary<string, object?>(defaultFiles)
        {
            ["/home/projects/TS/p2/tsconfig.json"] = """
                {
                			"compilerOptions": {
                				"noLib": true,
                				"module": "nodenext",
                				"strict": true,
                				"moduleDetection": "auto"
                			},
                			"include": ["src"]
                		}
                """,
            ["/home/projects/TS/p2/src/index.ts"] = """import { x } from "../../p1/src/x";""",
        };
        var (session, _) = Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p1/src/index.ts"), 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, new DocumentUri("file:///home/projects/TS/p2/src/index.ts"), 1, (string)files["/home/projects/TS/p2/src/index.ts"]!, LanguageKind.TypeScript);

            var snapshot = session.Snapshot();
            Assert.Equal(2, snapshot.ProjectCollection.Projects().Length);

            var program1 = Open(session, new DocumentUri("file:///home/projects/TS/p1/src/index.ts")).GetProgram();
            var program2 = Open(session, new DocumentUri("file:///home/projects/TS/p2/src/index.ts")).GetProgram();

            var x1 = program1!.GetSourceFile("/home/projects/TS/p1/src/x.ts");
            var x2 = program2!.GetSourceFile("/home/projects/TS/p1/src/x.ts");
            Assert.True(x1 is not null && x2 is not null);
            Assert.NotSame(x1, x2);
        }
        finally
        {
            session.Close();
        }
    }
}
