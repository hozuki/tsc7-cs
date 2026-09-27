// ported from: internal/project/projectlifetime_test.go:1-497
//
// 与 Go 测试的差异（逐条）：
// 1) `projecttestutil.Setup(files)` → `Harness.Setup(files)`；`bundled.Embedded` 跳过分支不移植（恒 true）。
// 2) `session.GetLanguageService(...)` 的双返回用 `ProjectTestSupport.Open(...)` 承载；Go 的 `assert.NilError` 同理。
// 3) `tspath.Path("/home/projects/ts/p1/tsconfig.json")` → **`utils.ToPath("…")`**：C# 的 `Path` 只是字符串包装，
//    Go 侧 `tspath.Path` 的大小写归一化在 C# 里由 `ToPath(fileName, currentDirectory, useCaseSensitiveFileNames)`
//    承担 ⇒ 用脚手架的 `SessionUtils.ToPath` 才是同一语义（Go 直接写 `tspath.Path(...)` 是因为 Go 的
//    `tspath.Path` 转换在大小写不敏感时也走归一化；C# 侧没有该构造函数语义）。
// 4) `len(utils.Client().WatchFilesCalls())` → `.Length`（脚手架返回调用记录的元组数组；`GetLocaleCalls()/IsActiveCalls()`
//    这两个 Go 侧只取 len 的才返回 int，见 ProjectTestHarness 的 API 说明）。
// 5) `ls.GetProgram().GetSourceFile(path)` → `program.GetSourceFile(path)`（C# 返回 `SourceFile?`）。
// 6) Go 的子测试 `t.Run` → 独立的 [Fact]；`runNewFileScenario(t, openBeforeCreateEvent)` 提成私有方法。
using System.Collections.Generic;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Tests.ProjectTestUtil;
using TypeScript.TSPath;
using Xunit;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;

namespace TypeScript.Tests;

public class ProjectLifetimeTests
{
    private static Dictionary<string, object?> P1P2P3FileTree(string homePath) => new()
    {
        [$"{homePath}/p1/tsconfig.json"] = """
        {
            "compilerOptions": {
                "noLib": true,
                "module": "nodenext",
                "strict": true
            },
            "include": ["src"]
        }
        """,
        [$"{homePath}/p1/src/index.ts"] = """import { x } from "./x";""",
        [$"{homePath}/p1/src/x.ts"] = "export const x = 1;",
        [$"{homePath}/p1/config.ts"] = "let x = 1, y = 2;",
        [$"{homePath}/p2/tsconfig.json"] = """
        {
            "compilerOptions": {
                "noLib": true,
                "module": "nodenext",
                "strict": true
            },
            "include": ["src"]
        }
        """,
        [$"{homePath}/p2/src/index.ts"] = """import { x } from "./x";""",
        [$"{homePath}/p2/src/x.ts"] = "export const x = 1;",
        [$"{homePath}/p2/config.ts"] = "let x = 1, y = 2;",
        [$"{homePath}/p3/tsconfig.json"] = """
        {
            "compilerOptions": {
                "noLib": true,
                "module": "nodenext",
                "strict": true
            },
            "include": ["src"]
        }
        """,
        [$"{homePath}/p3/src/index.ts"] = """import { x } from "./x";""",
        [$"{homePath}/p3/src/x.ts"] = "export const x = 1;",
        [$"{homePath}/p3/config.ts"] = "let x = 1, y = 2;",
    };

    [Fact]
    public void ConfiguredProject()
    {
        var files = P1P2P3FileTree("/home/projects/TS");
        var (session, utils) = Harness.Setup(files);
        try
        {
            var snapshot = session.Snapshot();
            Assert.Empty(snapshot.ProjectCollection.Projects());

            // Open files in two projects
            DocumentUri uri1 = "file:///home/projects/TS/p1/src/index.ts";
            DocumentUri uri2 = "file:///home/projects/TS/p2/src/index.ts";
            session.DidOpenFile(Context.Background, uri1, 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, uri2, 1, (string)files["/home/projects/TS/p2/src/index.ts"]!, LanguageKind.TypeScript);
            session.WaitForBackgroundTasks();
            snapshot = session.Snapshot();
            var projectCountAfterOpen = snapshot.ProjectCollection.Projects().Length;
            Assert.Equal(2, projectCountAfterOpen);
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p2/tsconfig.json")));
            Assert.Single(utils.Client().WatchFilesCalls());
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/TS/p2/tsconfig.json")));

            // Close p1 file and open p3 file
            session.DidCloseFile(Context.Background, uri1);
            DocumentUri uri3 = "file:///home/projects/TS/p3/src/index.ts";
            session.DidOpenFile(Context.Background, uri3, 1, (string)files["/home/projects/TS/p3/src/index.ts"]!, LanguageKind.TypeScript);
            session.WaitForBackgroundTasks();

            // Should still have two projects, but p1 replaced by p3
            snapshot = session.Snapshot();
            var projectCountAfterReplacement = snapshot.ProjectCollection.Projects().Length;
            Assert.Equal(2, projectCountAfterReplacement);
            Assert.Null(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p2/tsconfig.json")));
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p3/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/TS/p2/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/TS/p3/tsconfig.json")));
            Assert.Single(utils.Client().WatchFilesCalls());
            Assert.Empty(utils.Client().UnwatchFilesCalls());

            // Close p2 and p3 files, open p1 file again
            session.DidCloseFile(Context.Background, uri2);
            session.DidCloseFile(Context.Background, uri3);
            session.DidOpenFile(Context.Background, uri1, 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);
            session.WaitForBackgroundTasks();

            // Should have one project (p1)
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/TS/p2/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/TS/p3/tsconfig.json")));
            Assert.Single(utils.Client().WatchFilesCalls());
            Assert.Empty(utils.Client().UnwatchFilesCalls());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void UnrootedInferredProjects()
    {
        var files = P1P2P3FileTree("/home/projects/TS");
        foreach (var key in new List<string>(files.Keys))
        {
            if (key.EndsWith("tsconfig.json", System.StringComparison.Ordinal))
            {
                files.Remove(key);
            }
        }

        var (session, _) = Harness.Setup(files);
        try
        {
            var snapshot = session.Snapshot();
            Assert.Empty(snapshot.ProjectCollection.Projects());

            // Open files without workspace roots (empty string) - should create single inferred project
            DocumentUri uri1 = "file:///home/projects/TS/p1/src/index.ts";
            DocumentUri uri2 = "file:///home/projects/TS/p2/src/index.ts";
            session.DidOpenFile(Context.Background, uri1, 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, uri2, 1, (string)files["/home/projects/TS/p2/src/index.ts"]!, LanguageKind.TypeScript);

            // Should have one inferred project
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Close p1 file and open p3 file
            session.DidCloseFile(Context.Background, uri1);
            DocumentUri uri3 = "file:///home/projects/TS/p3/src/index.ts";
            session.DidOpenFile(Context.Background, uri3, 1, (string)files["/home/projects/TS/p3/src/index.ts"]!, LanguageKind.TypeScript);

            // Should still have one inferred project
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Close p2 and p3 files, open p1 file again
            session.DidCloseFile(Context.Background, uri2);
            session.DidCloseFile(Context.Background, uri3);
            session.DidOpenFile(Context.Background, uri1, 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            // Should still have one inferred project
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void FileMovesFromInferredToConfiguredProject()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/ts/foo.ts"] = "export const foo = 1;",
            ["/home/projects/ts/p1/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "noLib": true,
                    "module": "nodenext",
                    "strict": true
                },
                "include": ["main.ts"]
            }
            """,
            ["/home/projects/ts/p1/main.ts"] = """import { foo } from "../foo"; console.log(foo);""",
        };
        var (session, utils) = Harness.Setup(files);
        try
        {
            // Open foo.ts first - should create inferred project since no tsconfig found initially
            DocumentUri fooUri = "file:///home/projects/ts/foo.ts";
            session.DidOpenFile(Context.Background, fooUri, 1, (string)files["/home/projects/ts/foo.ts"]!, LanguageKind.TypeScript);

            // Should have one inferred project
            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());
            Assert.Null(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/ts/p1/tsconfig.json")));

            // Now open main.ts - should trigger discovery of tsconfig.json and move foo.ts to configured project
            DocumentUri mainUri = "file:///home/projects/ts/p1/main.ts";
            session.DidOpenFile(Context.Background, mainUri, 1, (string)files["/home/projects/ts/p1/main.ts"]!, LanguageKind.TypeScript);

            // Should now have one configured project and no inferred project
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.Null(snapshot.ProjectCollection.InferredProject());
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/ts/p1/tsconfig.json")));

            // Config file should be present
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/ts/p1/tsconfig.json")));

            // Close main.ts - configured project should remain because foo.ts is still open
            session.DidCloseFile(Context.Background, mainUri);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/ts/p1/tsconfig.json")));

            // Close foo.ts - configured project should be retained until next file open
            session.DidCloseFile(Context.Background, fooUri);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/projects/ts/p1/tsconfig.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void FileMoveFromInferredToConfiguredViaDidOpenDidCloseSequence()
    {
        // Start with tsconfig.json that includes "src" but file is at root level
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "noLib": true
                },
                "include": ["src"]
            }
            """,
            ["/home/projects/TS/p1/index.ts"] = "export const x = 1;",
        };
        var (session, utils) = Harness.Setup(files);
        try
        {
            // Open index.ts at root level - should create inferred project since it's not under src/
            // Creates config file registry entry, but has no files
            DocumentUri indexUri = "file:///home/projects/TS/p1/index.ts";
            session.DidOpenFile(Context.Background, indexUri, 1, (string)files["/home/projects/TS/p1/index.ts"]!, LanguageKind.TypeScript);

            // Should have one inferred project only (file is not included by tsconfig)
            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());
            Assert.Null(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));

            // Simulate file move: create src/index.ts on disk
            utils.FS().WriteFile("/home/projects/TS/p1/src/index.ts", (string)files["/home/projects/TS/p1/index.ts"]!);
            utils.FS().Remove("/home/projects/TS/p1/index.ts");

            // Simulate file move sequence as it would happen in an editor:
            // 1. didOpen src/index.ts (new location)
            // Open comes in before file create event, so the config file is not marked as needing a file name reload,
            // so it's not turned into a configured project yet. This is probably not ideal, but it should sort itself
            // out momentarily after the file watcher events are processed. When we try the config file, we mark it
            // as "retained by src/index.ts" so the config entry doesn't get deleted before src/index.ts is closed.
            // Even though we currently think src/index.ts doesn't belong to the config, the config is in its directory
            // path, so we'll always see it as a candidate for containing src/index.ts.
            DocumentUri srcIndexUri = "file:///home/projects/TS/p1/src/index.ts";
            session.DidOpenFile(Context.Background, srcIndexUri, 1, (string)files["/home/projects/TS/p1/index.ts"]!, LanguageKind.TypeScript);

            // 2. didClose index.ts (old location)
            session.DidCloseFile(Context.Background, indexUri);

            // 3. didChangeWatchedFiles: create src/index.ts and delete index.ts
            // The creation event for src/index.ts now hits the config file registry, and we should notice we
            // got a creation event for a file that retained the config, triggering a filename reload.
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Uri = srcIndexUri, Type = FileChangeType.Created },
                new FileEvent { Uri = indexUri, Type = FileChangeType.Deleted },
            ]);

            // Should now have one configured project only (file is now under src/)
            ProjectTestSupport.Open(session, Context.Background, srcIndexUri);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.Null(snapshot.ProjectCollection.InferredProject());
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>
    /// Regression test for https://github.com/microsoft/TypeScript/tsc/issues/3733
    ///
    /// "./"-prefixed include specs combined with a dot-directory exclude ("**/.*/")
    /// used to drop all wildcard directories for the config, so a newly created file
    /// never triggered a root file reload. The stale nested config never claimed the
    /// file, and default-project resolution assigned it to the ancestor project
    /// (parsed fresh from disk, so it did contain the file) — or the inferred project
    /// when no ancestor config matched — until the session was restarted. The file
    /// then had the wrong compiler options (e.g. no Temporal from lib ESNext).
    /// The failure was independent of the order in which didOpen and the create
    /// watch event arrived, so both orderings are covered.
    /// </summary>
    private static void RunNewFileScenario(bool openBeforeCreateEvent)
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/monorepo/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "target": "ES2015",
                    "lib": ["DOM"]
                },
                "include": ["apps"]
            }
            """,
            ["/home/projects/TS/monorepo/apps/web/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "target": "ESNext",
                    "lib": ["ESNext"]
                },
                "include": ["./app/**/*.ts"],
                "exclude": ["**/.*/"]
            }
            """,
            ["/home/projects/TS/monorepo/apps/web/app/existing.ts"] = "export const existing = 1;",
        };
        var (session, utils) = Harness.Setup(files);
        try
        {
            // Open an existing file so the nested configured project loads.
            DocumentUri existingUri = "file:///home/projects/TS/monorepo/apps/web/app/existing.ts";
            session.DidOpenFile(Context.Background, existingUri, 1, (string)files["/home/projects/TS/monorepo/apps/web/app/existing.ts"]!, LanguageKind.TypeScript);
            var ls = ProjectTestSupport.Open(session, Context.Background, existingUri);
            Assert.Equal(ScriptTarget.ESNext, ls.GetProgram()!.Options().Target);

            // Create a new file on disk in a directory matched by the nested config's
            // wildcard include, then simulate the editor events in the given order.
            const string newFileContent = "export const newFile = 1;";
            DocumentUri newFileUri = "file:///home/projects/TS/monorepo/apps/web/app/subdir/new.ts";
            utils.FS().WriteFile("/home/projects/TS/monorepo/apps/web/app/subdir/new.ts", newFileContent);
            void OpenNewFile()
            {
                session.DidOpenFile(Context.Background, newFileUri, 1, newFileContent, LanguageKind.TypeScript);
            }

            void SendCreateEvent()
            {
                session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = newFileUri, Type = FileChangeType.Created }]);
            }

            if (openBeforeCreateEvent)
            {
                OpenNewFile();
                SendCreateEvent();
            }
            else
            {
                SendCreateEvent();
                OpenNewFile();
            }

            // The new file should be served by the nested configured project, not the
            // root project (ES2015 target) or an inferred project.
            ls = ProjectTestSupport.Open(session, Context.Background, newFileUri);
            Assert.Equal(ScriptTarget.ESNext, ls.GetProgram()!.Options().Target);
            Assert.Null(session.Snapshot().ProjectCollection.InferredProject());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void NewlyCreatedFileJoinsConfiguredProjectWithPrefixedIncludesDidOpenBeforeCreateEvent()
    {
        RunNewFileScenario(openBeforeCreateEvent: true);
    }

    [Fact]
    public void NewlyCreatedFileJoinsConfiguredProjectWithPrefixedIncludesCreateEventBeforeDidOpen()
    {
        RunNewFileScenario(openBeforeCreateEvent: false);
    }

    [Fact]
    public void TsconfigMoveFromSubdirectoryToParentViaDidChangeWatchedFiles()
    {
        // Start with tsconfig.json in src/ that includes "src" - file won't be included initially
        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/src/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "noLib": true
                },
                "include": ["src"]
            }
            """,
            ["/home/projects/TS/p1/src/index.ts"] = "export const x = 1;",
            ["/home/projects/TS/p1/src/other.ts"] = "export const y = 2;",
        };
        var (session, utils) = Harness.Setup(files);
        try
        {
            // Open src/index.ts - should create inferred project since tsconfig.json includes "src"
            // relative to its location (src/src/ which doesn't exist)
            DocumentUri indexUri = "file:///home/projects/TS/p1/src/index.ts";
            session.DidOpenFile(Context.Background, indexUri, 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);

            // Should have one inferred project only (file is not included by tsconfig at src/tsconfig.json)
            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());
            Assert.Null(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p1/src/tsconfig.json")));

            // Simulate tsconfig.json move: create tsconfig.json at parent level, delete from src/
            var tsconfigContent = (string)files["/home/projects/TS/p1/src/tsconfig.json"]!;
            utils.FS().WriteFile("/home/projects/TS/p1/tsconfig.json", tsconfigContent);
            utils.FS().Remove("/home/projects/TS/p1/src/tsconfig.json");

            // Simulate file move via didChangeWatchedFiles
            DocumentUri newTsconfigUri = "file:///home/projects/TS/p1/tsconfig.json";
            DocumentUri oldTsconfigUri = "file:///home/projects/TS/p1/src/tsconfig.json";
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Uri = newTsconfigUri, Type = FileChangeType.Created },
                new FileEvent { Uri = oldTsconfigUri, Type = FileChangeType.Deleted },
            ]);
            session.WaitForBackgroundTasks();

            // The background update should route index.ts to the new configured project,
            // but project cleanup is deferred until the next file open.
            ProjectTestSupport.Open(session, Context.Background, indexUri);
            snapshot = session.Snapshot();
            var projectCountBeforeOpen = snapshot.ProjectCollection.Projects().Length;
            Assert.Equal(2, projectCountBeforeOpen);
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());
            Assert.Equal("/home/projects/TS/p1/tsconfig.json", snapshot.GetDefaultProject(indexUri)!.ConfigFileName());

            DocumentUri otherUri = "file:///home/projects/TS/p1/src/other.ts";
            session.DidOpenFile(Context.Background, otherUri, 1, (string)files["/home/projects/TS/p1/src/other.ts"]!, LanguageKind.TypeScript);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.Null(snapshot.ProjectCollection.InferredProject());
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/projects/TS/p1/tsconfig.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void DeletedOpenFileRemainsInProjectUntilClosed()
    {
        // Scenario:
        // 1. Start with two files included by a tsconfig, both open
        // 2. In a single batch change, delete one of the files but leave it open, and create a new file included by the tsconfig
        // 3. Request a LS for the deleted but open file
        // 4. Project should include both the new file and the deleted open file
        // 5. Close the deleted file
        // 6. On next LS request, the project should exclude the deleted file

        var files = new Dictionary<string, object?>
        {
            ["/home/projects/TS/p1/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "noLib": true
                },
                "include": ["src"]
            }
            """,
            ["/home/projects/TS/p1/src/index.ts"] = "",
            ["/home/projects/TS/p1/src/x.ts"] = "export const x = 1;",
        };
        var (session, utils) = Harness.Setup(files);
        try
        {
            // Step 1: Open both files
            DocumentUri indexUri = "file:///home/projects/TS/p1/src/index.ts";
            DocumentUri xUri = "file:///home/projects/TS/p1/src/x.ts";
            session.DidOpenFile(Context.Background, indexUri, 1, (string)files["/home/projects/TS/p1/src/index.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, xUri, 1, (string)files["/home/projects/TS/p1/src/x.ts"]!, LanguageKind.TypeScript);

            // Verify initial state - both files should be in the project
            var ls = ProjectTestSupport.Open(session, Context.Background, indexUri);
            var program = ls.GetProgram()!;
            Assert.NotNull(program.GetSourceFile("/home/projects/TS/p1/src/index.ts"));
            Assert.NotNull(program.GetSourceFile("/home/projects/TS/p1/src/x.ts"));

            // Step 2: In a single batch change:
            // - Delete x.ts from disk (but leave it open)
            // - Create a new file y.ts on disk
            utils.FS().Remove("/home/projects/TS/p1/src/x.ts");
            utils.FS().WriteFile("/home/projects/TS/p1/src/y.ts", "export const y = 2;");

            // Send both events in a single batch
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Uri = xUri, Type = FileChangeType.Deleted },
                new FileEvent { Uri = "file:///home/projects/TS/p1/src/y.ts", Type = FileChangeType.Created },
            ]);

            // Step 3 & 4: Request LS for the deleted but still open file
            // Project should include: index.ts, x.ts (open overlay), y.ts (new disk file)
            ls = ProjectTestSupport.Open(session, Context.Background, xUri);
            program = ls.GetProgram()!;
            Assert.NotNull(program.GetSourceFile("/home/projects/TS/p1/src/index.ts"));
            Assert.NotNull(program.GetSourceFile("/home/projects/TS/p1/src/x.ts"));
            Assert.NotNull(program.GetSourceFile("/home/projects/TS/p1/src/y.ts"));

            // Step 5: Close the deleted file
            session.DidCloseFile(Context.Background, xUri);

            // Step 6: On next LS request, x.ts should be excluded
            ls = ProjectTestSupport.Open(session, Context.Background, indexUri);
            program = ls.GetProgram()!;
            Assert.NotNull(program.GetSourceFile("/home/projects/TS/p1/src/index.ts"));
            Assert.Null(program.GetSourceFile("/home/projects/TS/p1/src/x.ts"));
            Assert.NotNull(program.GetSourceFile("/home/projects/TS/p1/src/y.ts"));
        }
        finally
        {
            session.Close();
        }
    }
}
