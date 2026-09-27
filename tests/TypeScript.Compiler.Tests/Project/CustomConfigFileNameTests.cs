// ported from: internal/project/customconfigfilename_test.go:1-233
//
// 与 Go 测试的差异（逐条）：
// 1) `projecttestutil.Setup(files)` → `Harness.Setup(files)`；`lsutil.NewDefaultUserPreferences()` →
//    `NewDefaultUserPreferences()`；`lsutil.ParseUserPreferences(map)` →
//    `UserPreferences.ParseUserPreferences(Dictionary<string, object?>)`（值类型 struct，字段 `CustomConfigFileName` 可写）。
// 2) `session.GetLanguageService` 的 error 用 `ProjectTestSupport.Open(...)` 承载（见 ProjectTests.cs 文件头第 2 条）；
//    Go 的 `_, err := …; assert.NilError(t, err)` → 一行 `ProjectTestSupport.Open(...)`（丢弃 LS 时也保留断言）。
// 3) `ls.GetProgram().Options().Strict` → `ls.GetProgram()!.Options().Strict`；`core.TSFalse/TSTrue` → `Tristate.TSFalse/TSTrue`。
// 4) `project.ID("/dev/null/inferred")` → `new ID("/dev/null/inferred")`（`ID` 是 `readonly record struct`，构造即 Go 的转换）。
// 5) `len(utils.Client().RefreshDiagnosticsCalls())` → 脚手架直接返回 int（见 ProjectTestHarness 的 API 说明）。
// 6) 子测试里的 `for _, invalidName := range []string{…}` → C# 的 `foreach`（每个循环体单独断言，失败信息带值）。
using System.Collections.Generic;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Project;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;
using static TypeScript.LsUtil.LsUtil;

namespace TypeScript.Tests;

public class CustomConfigFileNameTests
{
    private static readonly DocumentUri _uri = "file:///src/index.ts";

    private static Dictionary<string, object?> Files() => new()
    {
        ["/src/tsconfig.json"] = """{"compilerOptions": {"strict": false}}""",
        ["/src/tsconfig.all.json"] = """{"compilerOptions": {"strict": true}}""",
        ["/src/index.ts"] = "export const x = 1;",
    };

    [Fact]
    public void PicksUpCustomConfigAndSwitchesOnPreferenceChange()
    {
        var files = Files();
        var (session, _) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _uri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
            var ls = ProjectTestSupport.Open(session, Context.Background, _uri);

            var snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.json", snapshot.GetDefaultProject(_uri)!.ConfigFileName());
            Assert.Equal(Tristate.TSFalse, ls.GetProgram()!.Options().Strict);

            var prefs = NewDefaultUserPreferences();
            prefs.CustomConfigFileName = "tsconfig.all.json";
            session.Configure(prefs);

            ls = ProjectTestSupport.Open(session, Context.Background, _uri);

            snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.all.json", snapshot.GetDefaultProject(_uri)!.ConfigFileName());
            Assert.Equal(Tristate.TSTrue, ls.GetProgram()!.Options().Strict);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void UsesTsconfigJsonWhenCustomConfigFileNameIsEmpty()
    {
        var files = Files();
        var (session, _) = Harness.Setup(files);
        try
        {
            var prefs = NewDefaultUserPreferences();

            // default for CustomConfigFileName is "".
            Assert.Equal("", prefs.CustomConfigFileName);
            session.Configure(prefs);

            session.DidOpenFile(Context.Background, _uri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, Context.Background, _uri);

            var snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.json", snapshot.GetDefaultProject(_uri)!.ConfigFileName());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void FallsBackToTsconfigJsonWhenCustomConfigMissing()
    {
        var files = Files();
        var (session, _) = Harness.Setup(files);
        try
        {
            var prefs = NewDefaultUserPreferences();
            prefs.CustomConfigFileName = "tsconfig.nonexistent.json";
            session.Configure(prefs);

            session.DidOpenFile(Context.Background, _uri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, Context.Background, _uri);

            var snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.json", snapshot.GetDefaultProject(_uri)!.ConfigFileName());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void RevertsToTsconfigJsonWhenCustomConfigPreferenceIsCleared()
    {
        var files = Files();
        var (session, _) = Harness.Setup(files);
        try
        {
            // Step 1: Open file, verify it uses tsconfig.json (strict: false)
            session.DidOpenFile(Context.Background, _uri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
            var ls = ProjectTestSupport.Open(session, Context.Background, _uri);

            var snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.json", snapshot.GetDefaultProject(_uri)!.ConfigFileName());
            Assert.Equal(Tristate.TSFalse, ls.GetProgram()!.Options().Strict);

            // Step 2: Switch to custom config (strict: true)
            var prefs = NewDefaultUserPreferences();
            prefs.CustomConfigFileName = "tsconfig.all.json";
            session.Configure(prefs);

            ls = ProjectTestSupport.Open(session, Context.Background, _uri);

            snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.all.json", snapshot.GetDefaultProject(_uri)!.ConfigFileName());
            Assert.Equal(Tristate.TSTrue, ls.GetProgram()!.Options().Strict);

            // Step 3: Clear custom config preference, should revert to tsconfig.json (strict: false)
            prefs = NewDefaultUserPreferences();
            prefs.CustomConfigFileName = "";
            session.Configure(prefs);

            ls = ProjectTestSupport.Open(session, Context.Background, _uri);

            snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.json", snapshot.GetDefaultProject(_uri)!.ConfigFileName());
            Assert.Equal(Tristate.TSFalse, ls.GetProgram()!.Options().Strict);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>
    /// This test demonstrates the bug reported in #2020: after changing
    /// customConfigFileName, the server does not schedule a diagnostics refresh,
    /// so the VS Code client never knows to re-pull diagnostics and shows stale results.
    /// </summary>
    [Fact]
    public void SchedulesDiagnosticsRefreshWhenCustomConfigPreferenceChanges()
    {
        var files = Files();
        var (session, utils) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _uri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, Context.Background, _uri);
            session.WaitForBackgroundTasks();

            // Record baseline refresh call count
            var baselineRefreshCount = utils.Client().RefreshDiagnosticsCalls().Length;

            // Change the custom config preference
            var prefs = NewDefaultUserPreferences();
            prefs.CustomConfigFileName = "tsconfig.all.json";
            session.Configure(prefs);

            // GetLanguageService triggers the snapshot update with the new config
            ProjectTestSupport.Open(session, Context.Background, _uri);
            session.WaitForBackgroundTasks();

            // The server should have scheduled a diagnostics refresh to tell the client
            // to re-pull diagnostics with the new project configuration.
            var refreshCount = utils.Client().RefreshDiagnosticsCalls().Length;
            Assert.True(
                refreshCount > baselineRefreshCount,
                $"expected RefreshDiagnostics to be called after customConfigFileName change, got {refreshCount} calls (baseline {baselineRefreshCount})");
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void RejectsPathTraversalInCustomConfigFileName()
    {
        foreach (var invalidName in new[]
        {
            "/etc/passwd",
            "../tsconfig.json",
            "configs/tsconfig.all.json",
            @"..\tsconfig.json",
            @"sub\dir\tsconfig.json",
            "..",
            ".",
        })
        {
            var prefs = ParseUserPreferences(new Dictionary<string, object?>
            {
                ["js/ts"] = new Dictionary<string, object?>
                {
                    ["customConfigFileName"] = invalidName,
                },
            });
            Assert.Equal("", prefs.CustomConfigFileName);
        }
    }

    [Fact]
    public void AcceptsPlainBaseFileNamesInCustomConfigFileName()
    {
        foreach (var validName in new[]
        {
            "tsconfig.all.json",
            "tsconfig.editor.json",
            "jsconfig.custom.json",
        })
        {
            var prefs = ParseUserPreferences(new Dictionary<string, object?>
            {
                ["js/ts"] = new Dictionary<string, object?>
                {
                    ["customConfigFileName"] = validName,
                },
            });
            Assert.Equal(validName, prefs.CustomConfigFileName);
        }
    }

    [Fact]
    public void CleansUpInferredProjectWhenCustomConfigCoversFile()
    {
        // Start without any tsconfig.json so file goes into inferred project, then
        // add a custom config that covers the file and verify it moves out of the
        // inferred project (not just getting a new default, but actually cleaned up).
        var files = new Dictionary<string, object?>
        {
            ["/src/tsconfig.all.json"] = """{"compilerOptions": {"strict": true}, "include": ["./**/*"]}""",
            ["/src/index.ts"] = "export const x = 1;",
        };
        var (session, _) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _uri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, Context.Background, _uri);

            // Without any config, the file should be in the inferred project only.
            var snapshot = session.Snapshot();
            Assert.Equal(new ID("/dev/null/inferred"), snapshot.GetDefaultProject(_uri)!.ID());
            var projects = snapshot.GetLanguageServiceProjectsContainingFile(_uri);
            Assert.Single(projects);

            // Now set custom config to pick up tsconfig.all.json
            var prefs = NewDefaultUserPreferences();
            prefs.CustomConfigFileName = "tsconfig.all.json";
            session.Configure(prefs);

            ProjectTestSupport.Open(session, Context.Background, _uri);

            // File should now be in the configured project only, not duplicated in inferred.
            snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.all.json", snapshot.GetDefaultProject(_uri)!.ConfigFileName());
            projects = snapshot.GetLanguageServiceProjectsContainingFile(_uri);
            Assert.Single(projects);
        }
        finally
        {
            session.Close();
        }
    }
}
