// ported from: internal/project/projectreferencesprogram_test.go:1-540
//
// 与 Go 测试的差异（逐条）：
// 1) `projecttestutil.Setup(files)` → `Harness.Setup(files)`；`bundled.Embedded` 跳过分支不移植（恒 true）。
// 2) `core.IfElse(cond, a, b)` → `IfElse(cond, a, b)`（`using static TypeScript.Core.Core;`）；
//    `core.ScriptTargetESNext` → `ScriptTarget.ESNext`。
// 3) `lsconv.FileNameToDocumentURI(name)` → `TypeScript.LsConv.LsConv.FileNameToDocumentURI(name)`（包级函数落在静态类
//    `LsConv` 上；从 `TypeScript.Tests` 里 `LsConv` 会命中命名空间 ⇒ 必须全限定到类）。
// 4) `projecttestutil.WithRequestID(t.Context())` → `Harness.WithRequestID(Context.Background)`
//    （Go 的 `t.Context()` 在 xunit 里没有对应物，见 ProjectTests.cs 文件头第 3 条）。
// 5) `p.Program.GetSourceFileByPath(tspath.Path("…"))` → `p.Program!.GetSourceFileByPath(new Path("…"))`；
//    `p.Program.GetSourceFile(name)` → `GetSourceFile(name)`（C# 里两者都返回 `SourceFile?`）。
// 6) `vfstest.Symlink(target)` → `VfsTest.Symlink(target)`（脚手架的文件树值支持 `MapFile`）。
// 7) `addConfigForPackage` 里 Go 用 `core.StringifyJson(map, "    ", "  ")` 生成 JSON；C# 侧直接拼出等价的
//    JSON 文本（内容不被断言，配置语义一致）—— 逐字偏离见该方法注释。
using System.Collections.Generic;
using System.Linq;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.Tests.ProjectTestUtil;
using TypeScript.Vfs.VfsTest;
using Xunit;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;

using Path = TypeScript.TSPath.Path;

using static TypeScript.Core.Core;

namespace TypeScript.Tests;

public class ProjectReferencesProgramTests
{
    private static Dictionary<string, object?> FilesForReferencedProjectProgram(bool disableSourceOfProjectReferenceRedirect)
    {
        var extra = IfElse(disableSourceOfProjectReferenceRedirect, """, "disableSourceOfProjectReferenceRedirect": true""", "");
        return new Dictionary<string, object?>
        {
            ["/user/username/projects/myproject/main/tsconfig.json"] = $$"""
            {
                "compilerOptions": {
                    "composite": true{{extra}}
                },
                "references": [{ "path": "../dependency" }]
            }
            """,
            ["/user/username/projects/myproject/main/main.ts"] = """
            import {
                fn1,
                fn2,
                fn3,
                fn4,
                fn5
            } from '../decls/fns'
            fn1();
            fn2();
            fn3();
            fn4();
            fn5();
            """,
            ["/user/username/projects/myproject/dependency/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "composite": true,
                    "declarationDir": "../decls"
                },
            }
            """,
            ["/user/username/projects/myproject/dependency/fns.ts"] = """
            export function fn1() { }
            export function fn2() { }
            export function fn3() { }
            export function fn4() { }
            export function fn5() { }
            """,
        };
    }

    [Fact]
    public void ProgramForReferencedProject()
    {
        var files = FilesForReferencedProjectProgram(false);
        var (session, _) = Harness.Setup(files);
        try
        {
            var snapshot = session.Snapshot();
            Assert.Empty(snapshot.ProjectCollection.Projects());

            DocumentUri uri = "file:///user/username/projects/myproject/main/main.ts";
            session.DidOpenFile(Context.Background, uri, 1, (string)files["/user/username/projects/myproject/main/main.ts"]!, LanguageKind.TypeScript);

            snapshot = session.Snapshot();
            var projects = snapshot.ProjectCollection.Projects();
            Assert.Single(projects);
            var p = projects[0];
            Assert.Equal(Kind.KindConfigured, p.Kind);

            Assert.NotNull(p.Program!.GetSourceFileByPath(new Path("/user/username/projects/myproject/dependency/fns.ts")));
            Assert.Null(p.Program!.GetSourceFileByPath(new Path("/user/username/projects/myproject/decls/fns.d.ts")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ProgramWithDisableSourceOfProjectReferenceRedirect()
    {
        var files = FilesForReferencedProjectProgram(true);
        files["/user/username/projects/myproject/decls/fns.d.ts"] = """
            export declare function fn1(): void;
            export declare function fn2(): void;
            export declare function fn3(): void;
            export declare function fn4(): void;
            export declare function fn5(): void;
        """;
        var (session, _) = Harness.Setup(files);
        try
        {
            var snapshot = session.Snapshot();
            Assert.Empty(snapshot.ProjectCollection.Projects());

            DocumentUri uri = "file:///user/username/projects/myproject/main/main.ts";
            session.DidOpenFile(Context.Background, uri, 1, (string)files["/user/username/projects/myproject/main/main.ts"]!, LanguageKind.TypeScript);

            snapshot = session.Snapshot();
            var projects = snapshot.ProjectCollection.Projects();
            Assert.Single(projects);
            var p = projects[0];
            Assert.Equal(Kind.KindConfigured, p.Kind);

            Assert.Null(p.Program!.GetSourceFileByPath(new Path("/user/username/projects/myproject/dependency/fns.ts")));
            Assert.NotNull(p.Program!.GetSourceFileByPath(new Path("/user/username/projects/myproject/decls/fns.d.ts")));
        }
        finally
        {
            session.Close();
        }
    }

    private static void AssertSymlinkReferencesCase(
        (Dictionary<string, object?> Files, string ATest, string BFoo, string BBar) fixture)
    {
        var (files, aTest, bFoo, bBar) = fixture;
        var (session, _) = Harness.Setup(files);
        try
        {
            var snapshot = session.Snapshot();
            Assert.Empty(snapshot.ProjectCollection.Projects());

            var uri = TypeScript.LsConv.LsConv.FileNameToDocumentURI(aTest);
            session.DidOpenFile(Context.Background, uri, 1, (string)files[aTest]!, LanguageKind.TypeScript);

            snapshot = session.Snapshot();
            var projects = snapshot.ProjectCollection.Projects();
            Assert.Single(projects);
            var p = projects[0];
            Assert.Equal(Kind.KindConfigured, p.Kind);

            Assert.NotNull(p.Program!.GetSourceFile(bFoo));
            Assert.NotNull(p.Program!.GetSourceFile(bBar));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ReferencesThroughSymlinkWithIndexAndTypings() => AssertSymlinkReferencesCase(FilesForSymlinkReferences(false, ""));

    [Fact]
    public void ReferencesThroughSymlinkWithIndexAndTypingsWithPreserveSymlinks() => AssertSymlinkReferencesCase(FilesForSymlinkReferences(true, ""));

    [Fact]
    public void ReferencesThroughSymlinkWithIndexAndTypingsScopedPackage() => AssertSymlinkReferencesCase(FilesForSymlinkReferences(false, "@issue/"));

    [Fact]
    public void ReferencesThroughSymlinkWithIndexAndTypingsWithScopedPackagePreserveSymlinks() => AssertSymlinkReferencesCase(FilesForSymlinkReferences(true, "@issue/"));

    [Fact]
    public void ReferencesThroughSymlinkReferencingFromSubFolder() => AssertSymlinkReferencesCase(FilesForSymlinkReferencesInSubfolder(false, ""));

    [Fact]
    public void ReferencesThroughSymlinkReferencingFromSubFolderWithPreserveSymlinks() => AssertSymlinkReferencesCase(FilesForSymlinkReferencesInSubfolder(true, ""));

    [Fact]
    public void ReferencesThroughSymlinkReferencingFromSubFolderScopedPackage() => AssertSymlinkReferencesCase(FilesForSymlinkReferencesInSubfolder(false, "@issue/"));

    [Fact]
    public void ReferencesThroughSymlinkReferencingFromSubFolderWithScopedPackagePreserveSymlinks() => AssertSymlinkReferencesCase(FilesForSymlinkReferencesInSubfolder(true, "@issue/"));

    private static void AssertDirectorySubpathSymlinkCase(string scope)
    {
        var (files, aIndex, bFile) = FilesForDirectorySubpathSymlinkReferences(scope);
        var (session, _) = Harness.Setup(files);
        try
        {
            var uri = TypeScript.LsConv.LsConv.FileNameToDocumentURI(aIndex);
            session.DidOpenFile(Context.Background, uri, 1, (string)files[aIndex]!, LanguageKind.TypeScript);

            var snapshot = session.Snapshot();
            var projects = snapshot.ProjectCollection.Projects();
            Assert.Single(projects);
            var p = projects[0];
            Assert.Equal(Kind.KindConfigured, p.Kind);

            // The import must redirect to source, so the source file is part of the program...
            Assert.NotNull(p.Program!.GetSourceFile(bFile));
            // ...and there must be no `TS2307: Cannot find module 'b/lib/File'` diagnostic.
            var diagnostics = p.Program!.GetSemanticDiagnostics(Harness.WithRequestID(Context.Background), p.Program!.GetSourceFile(aIndex));
            Assert.Empty(diagnostics);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ReferencesThroughSymlinkWithDirectoryIndexSubpath() => AssertDirectorySubpathSymlinkCase("");

    [Fact]
    public void ReferencesThroughSymlinkWithDirectoryIndexSubpathScopedPackage() => AssertDirectorySubpathSymlinkCase("@issue/");

    [Fact]
    public void WhenNewFileIsAddedToReferencedProject()
    {
        var files = FilesForReferencedProjectProgram(false);
        var (session, utils) = Harness.Setup(files);
        try
        {
            DocumentUri uri = "file:///user/username/projects/myproject/main/main.ts";
            session.DidOpenFile(Context.Background, uri, 1, (string)files["/user/username/projects/myproject/main/main.ts"]!, LanguageKind.TypeScript);
            var snapshot = session.Snapshot();
            var projects = snapshot.ProjectCollection.Projects();
            Assert.Single(projects);
            var programBefore = projects[0].Program;

            utils.FS().WriteFile("/user/username/projects/myproject/dependency/fns2.ts", "export const x = 2;");
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Type = FileChangeType.Created, Uri = "file:///user/username/projects/myproject/dependency/fns2.ts" },
            ]);

            ProjectTestSupport.Open(session, Context.Background, uri);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotSame(programBefore, snapshot.ProjectCollection.Projects()[0].Program);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>
    /// Regression test for https://github.com/microsoft/TypeScript/tsc/issues/3942.
    /// A project reference dropped from a tsconfig used to leave a stale entry in
    /// the referenced config's retainingProjects. When the referencing project was
    /// later deleted and the referenced config subsequently changed, the stale entry
    /// named a project that no longer existed, crashing markProjectsAffectedByConfigChanges.
    /// </summary>
    [Fact]
    public void DroppedProjectReferenceDoesNotCrashOnLaterChangeToTheDroppedConfig()
    {
        var files = new Dictionary<string, object?>
        {
            ["/user/username/projects/myproject/main/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "composite": true
                },
                "references": [{ "path": "../dependency" }]
            }
            """,
            ["/user/username/projects/myproject/main/main.ts"] = """
            import { fn1 } from '../dependency/fns'
            fn1();
            """,
            ["/user/username/projects/myproject/dependency/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "composite": true
                }
            }
            """,
            ["/user/username/projects/myproject/dependency/fns.ts"] = """
            export function fn1() { }
            """,
            ["/user/username/projects/myproject/other/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "composite": true
                }
            }
            """,
            ["/user/username/projects/myproject/other/other.ts"] = "export const y = 1;",
        };

        var (session, utils) = Harness.Setup(files);
        try
        {
            DocumentUri mainUri = "file:///user/username/projects/myproject/main/main.ts";
            var mainContent = (string)files["/user/username/projects/myproject/main/main.ts"]!;
            DocumentUri otherUri = "file:///user/username/projects/myproject/other/other.ts";
            var otherContent = (string)files["/user/username/projects/myproject/other/other.ts"]!;

            // 1. Open main.ts. The main project's program resolves the `../dependency`
            //    project reference, so the dependency config's retainingProjects gains
            //    the main project path.
            session.DidOpenFile(Context.Background, mainUri, 1, mainContent, LanguageKind.TypeScript);
            session.WaitForBackgroundTasks();
            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(new Path("/user/username/projects/myproject/dependency/tsconfig.json")));

            // 2. Remove the project reference from main/tsconfig.json and rebuild.
            //    The new program no longer references dependency.
            utils.FS().WriteFile("/user/username/projects/myproject/main/tsconfig.json", """
            {
                "compilerOptions": {
                    "composite": true
                }
            }
            """);
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Type = FileChangeType.Changed, Uri = "file:///user/username/projects/myproject/main/tsconfig.json" },
            ]);
            ProjectTestSupport.Open(session, Context.Background, mainUri);

            // 3. Close main.ts and open an unrelated file. Opening triggers cleanup of
            //    orphaned configured projects, deleting the main project.
            session.DidCloseFile(Context.Background, mainUri);
            session.DidOpenFile(Context.Background, otherUri, 1, otherContent, LanguageKind.TypeScript);
            session.WaitForBackgroundTasks();
            snapshot = session.Snapshot();
            Assert.Null(snapshot.ProjectCollection.ConfiguredProject(new Path("/user/username/projects/myproject/main/tsconfig.json")));

            // Dropping the reference releases main from dependency's retainingProjects,
            // so the now-unreferenced dependency config is cleaned up and no stale entry
            // survives to crash a later config change.
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(new Path("/user/username/projects/myproject/dependency/tsconfig.json")));

            // 4. Change dependency/tsconfig.json and flush. This used to copy the stale
            //    retainingProjects into affectedProjects and crash
            //    markProjectsAffectedByConfigChanges loading the deleted main project.
            utils.FS().WriteFile("/user/username/projects/myproject/dependency/tsconfig.json", """
            {
                "compilerOptions": {
                    "composite": true,
                    "strict": true
                }
            }
            """);
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Type = FileChangeType.Changed, Uri = "file:///user/username/projects/myproject/dependency/tsconfig.json" },
            ]);
            ProjectTestSupport.Open(session, Context.Background, otherUri);
            session.WaitForBackgroundTasks();
        }
        finally
        {
            session.Close();
        }
    }

    private static (Dictionary<string, object?> Files, string ATest, string BFoo, string BBar) FilesForSymlinkReferences(bool preserveSymlinks, string scope)
    {
        var aTest = "/user/username/projects/myproject/packages/A/src/index.ts";
        var bFoo = "/user/username/projects/myproject/packages/B/src/index.ts";
        var bBar = "/user/username/projects/myproject/packages/B/src/bar.ts";
        var files = new Dictionary<string, object?>
        {
            ["/user/username/projects/myproject/packages/B/package.json"] = """
            {
                "main": "lib/index.js",
                "types": "lib/index.d.ts"
            }
            """,
            [aTest] = $$"""
            import { foo } from '{{scope}}b';
            import { bar } from '{{scope}}b/lib/bar';
            foo();
            bar();
            """,
            [bFoo] = "export function foo() { }",
            [bBar] = "export function bar() { }",
            [$"/user/username/projects/myproject/node_modules/{scope}b"] = VfsTest.Symlink("/user/username/projects/myproject/packages/B"),
        };
        AddConfigForPackage(files, "A", preserveSymlinks, ["../B"]);
        AddConfigForPackage(files, "B", preserveSymlinks, null);
        return (files, aTest, bFoo, bBar);
    }

    private static (Dictionary<string, object?> Files, string ATest, string BFoo, string BBar) FilesForSymlinkReferencesInSubfolder(bool preserveSymlinks, string scope)
    {
        var aTest = "/user/username/projects/myproject/packages/A/src/test.ts";
        var bFoo = "/user/username/projects/myproject/packages/B/src/foo.ts";
        var bBar = "/user/username/projects/myproject/packages/B/src/bar/foo.ts";
        var files = new Dictionary<string, object?>
        {
            ["/user/username/projects/myproject/packages/B/package.json"] = "{}",
            [aTest] = $$"""
            import { foo } from '{{scope}}b/lib/foo';
            import { bar } from '{{scope}}b/lib/bar/foo';
            foo();
            bar();
            """,
            [bFoo] = "export function foo() { }",
            [bBar] = "export function bar() { }",
            [$"/user/username/projects/myproject/node_modules/{scope}b"] = VfsTest.Symlink("/user/username/projects/myproject/packages/B"),
        };
        AddConfigForPackage(files, "A", preserveSymlinks, ["../B"]);
        AddConfigForPackage(files, "B", preserveSymlinks, null);
        return (files, aTest, bFoo, bBar);
    }

    private static (Dictionary<string, object?> Files, string AIndex, string BFile) FilesForDirectorySubpathSymlinkReferences(string scope)
    {
        var aIndex = "/user/username/projects/myproject/packages/a/src/index.ts";
        var bFile = "/user/username/projects/myproject/packages/b/src/File/index.ts";
        var files = new Dictionary<string, object?>
        {
            ["/user/username/projects/myproject/packages/b/package.json"] = """
            {
                "main": "lib/index.js",
                "types": "lib/index.d.ts"
            }
            """,
            [aIndex] = $$"""
            import { helper } from "{{scope}}b/lib/File";
            export const result: number = helper();
            """,
            [bFile] = "export function helper(): number { return 1; }",
            [$"/user/username/projects/myproject/node_modules/{scope}b"] = VfsTest.Symlink("/user/username/projects/myproject/packages/b"),
        };
        AddConfigForPackage(files, "a", false, ["../b"]);
        AddConfigForPackage(files, "b", false, null);
        return (files, aIndex, bFile);
    }

    /// <summary>
    /// Go 用 <c>core.StringifyJson(map, "    ", "  ")</c> 生成 tsconfig 文本；这里直接拼出等价 JSON
    /// （配置内容不被断言，语义一致；见文件头第 7 条）。
    /// </summary>
    private static void AddConfigForPackage(Dictionary<string, object?> files, string packageName, bool preserveSymlinks, string[]? references)
    {
        var options = new List<string> { "\"outDir\": \"lib\"", "\"rootDir\": \"src\"", "\"composite\": true" };
        if (preserveSymlinks)
        {
            options.Add("\"preserveSymlinks\": true");
        }

        var referencesToAdd = string.Join(", ", (references ?? []).Select(reference => $"{{ \"path\": \"{reference}\" }}"));
        files[$"/user/username/projects/myproject/packages/{packageName}/tsconfig.json"] = $$"""
        {
            "compilerOptions": { {{string.Join(", ", options)}} },
            "include": ["src"],
            "references": [{{referencesToAdd}}]
        }
        """;
    }
}
