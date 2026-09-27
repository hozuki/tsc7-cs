// ported from: internal/project/configfilechanges_test.go:1-201
//
// 与 Go 测试的差异（逐条）：
// 1) `projecttestutil.Setup(files)` → `Harness.Setup(files)`（namespace `TypeScript.Tests.ProjectTestUtil`）；
//    Go 的 `t.Parallel()`/`bundled.Embedded` 跳过分支在 xunit/本仓下不可达（见 ProjectTests.cs 文件头第 1 条），不移植。
// 2) `utils.FS().WriteFile/Remove` 在 C# 侧是 **void（失败抛异常）** ⇒ Go 的 `err := …; assert.NilError(t, err)` 只需一行调用。
// 3) `session.GetLanguageService(...)` 保留双返回 `(LanguageService?, Exception?)` ⇒ 用既有的
//    `ProjectTestSupport.Open(session, ctx, uri)`（断言无 error 并取 LS）。
// 4) `ls.GetProgram().Options().Target` → `ls.GetProgram()!.Options().Target`；`core.ScriptTargetESNext` →
//    `ScriptTarget.ESNext`；`core.TSFalse/TSTrue` → `Tristate.TSFalse/TSTrue`。
// 5) `assert.Assert(t, x != y)`（指针不等）→ `Assert.NotSame(y, x)`。
// 6) Go 直接读 `files["…"].(string)` → C# `(string)files["…"]!`。
using System.Collections.Generic;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;

namespace TypeScript.Tests;

public class ConfigFileChangesTests
{
    private static readonly DocumentUri _indexUri = "file:///src/index.ts";
    private static readonly DocumentUri _subfolderUri = "file:///src/subfolder/foo.ts";

    private static Dictionary<string, object?> Files() => new()
    {
        ["/tsconfig.more-base.json"] = "{}",
        ["/tsconfig.base.json"] = """{"extends": "../tsconfig.more-base.json", "compilerOptions": {"strict": true}}""",
        ["/src/tsconfig.json"] = """{"extends": "../tsconfig.base.json", "compilerOptions": {"target": "es6"}, "references": [{"path": "../utils"}]}""",
        ["/src/index.ts"] = """console.log("Hello, world!");""",
        ["/src/subfolder/foo.ts"] = """export const foo = "bar";""",
        ["/utils/tsconfig.json"] = """{"compilerOptions": {"composite": true}}""",
        ["/utils/index.ts"] = """console.log("Hello, test!");""",
    };

    [Fact]
    public void ShouldUpdateProgramOptionsOnConfigFileChange()
    {
        var files = Files();
        var (session, utils) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _indexUri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);

            utils.FS().WriteFile("/src/tsconfig.json", """{"extends": "../tsconfig.base.json", "compilerOptions": {"target": "esnext"}, "references": [{"path": "../utils"}]}""");
            session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///src/tsconfig.json", Type = FileChangeType.Changed }]);

            var ls = ProjectTestSupport.Open(session, Context.Background, _indexUri);
            Assert.Equal(ScriptTarget.ESNext, ls.GetProgram()!.Options().Target);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ShouldUpdateProjectOnExtendedConfigFileChange()
    {
        var files = Files();
        var (session, utils) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _indexUri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);

            utils.FS().WriteFile("/tsconfig.base.json", """{"compilerOptions": {"strict": false}}""");
            session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///tsconfig.base.json", Type = FileChangeType.Changed }]);

            var ls = ProjectTestSupport.Open(session, Context.Background, _indexUri);
            Assert.Equal(Tristate.TSFalse, ls.GetProgram()!.Options().Strict);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ShouldUpdateProjectOnDoublyExtendedConfigFileChange()
    {
        var files = Files();
        var (session, utils) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _indexUri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);

            utils.FS().WriteFile("/tsconfig.more-base.json", """{"compilerOptions": {"verbatimModuleSyntax": true}}""");
            session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///tsconfig.more-base.json", Type = FileChangeType.Changed }]);

            var ls = ProjectTestSupport.Open(session, Context.Background, _indexUri);
            Assert.Equal(Tristate.TSTrue, ls.GetProgram()!.Options().VerbatimModuleSyntax);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ShouldUpdateProjectOnReferencedConfigFileChange()
    {
        var files = Files();
        var (session, utils) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _indexUri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
            var snapshotBefore = session.Snapshot();

            utils.FS().WriteFile("/utils/tsconfig.json", """{"compilerOptions": {"composite": true, "target": "esnext"}}""");
            session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///utils/tsconfig.json", Type = FileChangeType.Changed }]);

            ProjectTestSupport.Open(session, Context.Background, _indexUri);
            var snapshotAfter = session.Snapshot();
            Assert.NotSame(snapshotBefore, snapshotAfter);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ShouldCloseProjectOnConfigFileDeletion()
    {
        var files = Files();
        var (session, utils) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _indexUri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);

            utils.FS().Remove("/src/tsconfig.json");
            session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///src/tsconfig.json", Type = FileChangeType.Deleted }]);

            ProjectTestSupport.Open(session, Context.Background, _indexUri);
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
    public void ConfigFileCreationThenDeletion()
    {
        var files = Files();
        var (session, utils) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _subfolderUri, 1, (string)files["/src/subfolder/foo.ts"]!, LanguageKind.TypeScript);

            utils.FS().WriteFile("/src/subfolder/tsconfig.json", "{}");
            session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///src/subfolder/tsconfig.json", Type = FileChangeType.Created }]);

            ProjectTestSupport.Open(session, Context.Background, _subfolderUri);
            var snapshot = session.Snapshot();
            Assert.Equal(2, snapshot.ProjectCollection.Projects().Length);
            Assert.Equal("/src/subfolder/tsconfig.json", snapshot.GetDefaultProject(_subfolderUri)!.ConfigFileName());

            utils.FS().Remove("/src/subfolder/tsconfig.json");
            session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///src/subfolder/tsconfig.json", Type = FileChangeType.Deleted }]);

            ProjectTestSupport.Open(session, Context.Background, _subfolderUri);
            snapshot = session.Snapshot();
            Assert.Equal("/src/tsconfig.json", snapshot.GetDefaultProject(_subfolderUri)!.ConfigFileName());

            // Old project will be cleaned up on next file open
            Assert.Equal(2, snapshot.ProjectCollection.Projects().Length);

            session.DidOpenFile(Context.Background, _indexUri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ShouldUpdateProjectWhenMissingExtendedConfigIsCreated()
    {
        // Start with a project whose tsconfig extends a base config that doesn't exist yet
        var files = Files();
        files.Remove("/tsconfig.base.json");
        var (session, utils) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, _indexUri, 1, (string)files["/src/index.ts"]!, LanguageKind.TypeScript);

            // Create the previously-missing base config file that is extended by /src/tsconfig.json
            utils.FS().WriteFile("/tsconfig.base.json", """{"compilerOptions": {"strict": true}}""");
            session.DidChangeWatchedFiles(Context.Background, [new FileEvent { Uri = "file:///tsconfig.base.json", Type = FileChangeType.Created }]);

            // Accessing the language service should trigger project update
            var ls = ProjectTestSupport.Open(session, Context.Background, _indexUri);
            Assert.Equal(Tristate.TSTrue, ls.GetProgram()!.Options().Strict);
        }
        finally
        {
            session.Close();
        }
    }
}
