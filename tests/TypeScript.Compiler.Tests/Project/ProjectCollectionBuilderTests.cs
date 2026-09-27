// ported from: internal/project/projectcollectionbuilder_test.go:1-663
//
// 本文件有两部分：
//   A) `ProjectCollectionBuilderTests`：Go 该文件的 13 个 session 级子测试（走 `ProjectTestUtil.Setup`）；
//   B) `ProjectCollectionBuilderLayerTests`：不依赖 Session 的 builder 层契约（Finalize 复用、自定义配置名失效、
//      configuredProjectsOnly 不建 inferred project、空 summary 的 DidChangeFiles）—— Go 侧没有对应的独立
//      测试文件（它由 session 级用例间接覆盖），这里补上以便在 Session 尚未可用时也能守住 builder 契约。
//
// 与 Go 测试的差异（逐条）：
// 1) `projecttestutil.Setup(files)` → `Harness.Setup(files)`；`bundled.Embedded` 跳过分支不移植（恒 true）。
// 2) `session.GetLanguageService` 的双返回 → `ProjectTestSupport.Open(...)`（断言无 error）；Go 的 `assert.Equal(t, requestSnapshot, snapshot)`
//    是指针相等 → C# `Assert.Same(snapshot, requestSnapshot)`。
// 3) `tspath.Path("…")` → `utils.ToPath("…")`（C# 的 `Path` 只是字符串包装；大小写归一化在 `ToPath` 里，见
//    ProjectLifetimeTests.cs 文件头第 3 条）。这些用例里 Go 传的是全小写路径，正好依赖该归一化。
// 4) `p.Kind == project.KindConfigured/KindInferred` → `Kind.KindConfigured/KindInferred`（`Project.Kind` 是 public 字段、
//    `Kind` 是 `TypeScript.Project` 里的枚举）。
// 5) `assert.DeepEqual(inferredProject.Program.CommandLine().FileNames(), []string{…})` → `Assert.Equal(expected, actual)`
//    （xunit 对数组做逐元素比较）。
// 6) `filesForSolutionConfigFile`/`applyIndirectProjectFiles`/`filesForIndirectProject` 里的 `fmt.Sprintf` + `strings.Join`
//    → C# 内插字符串 + `string.Join`（生成的 tsconfig 文本语义一致，不被断言）。
// 7) `core.Map(refs, f)` → `string.Join(",", refs.Select(f))`（同 ProjectReferencesProgramTests.cs 的处理）。
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

namespace TypeScript.Tests;

public class ProjectCollectionBuilderTests
{
    private const string _myProjectRoot = "/user/username/projects/myproject";
    private const string _mainUri = "file://" + _myProjectRoot + "/src/main.ts";
    private const string _dummyUri = "file:///user/username/workspaces/dummy/dummy.ts";

    private static void OpenMainFile(Session session, Dictionary<string, object?> files)
    {
        session.DidOpenFile(Context.Background, _mainUri, 1, (string)files[_myProjectRoot + "/src/main.ts"]!, LanguageKind.TypeScript);
    }

    private static void CloseMainFileAndOpenDummy(Session session)
    {
        session.DidCloseFile(Context.Background, _mainUri);
        session.DidOpenFile(Context.Background, _dummyUri, 1, "const x = 1;", LanguageKind.TypeScript);
    }

    [Fact]
    public void WhenProjectFoundIsSolutionReferencingDefaultProjectDirectly()
    {
        var files = FilesForSolutionConfigFile(["./tsconfig-src.json"], "", null);
        var (session, utils) = Harness.Setup(files);
        try
        {
            OpenMainFile(session, files);

            // Ensure configured project is found for open file
            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Ensure request can use existing snapshot
            ProjectTestSupport.Open(session, Context.Background, _mainUri);
            var requestSnapshot = session.Snapshot();
            Assert.Same(snapshot, requestSnapshot);

            // Searched configs should be present while file is open
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Close the file and open one in an inferred project
            CloseMainFileAndOpenDummy(session);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Config files should have been released
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void WhenProjectFoundIsSolutionReferencingDefaultProjectIndirectly()
    {
        var files = FilesForSolutionConfigFile(["./tsconfig-indirect1.json", "./tsconfig-indirect2.json"], "", null);
        ApplyIndirectProjectFiles(files, 1, "");
        ApplyIndirectProjectFiles(files, 2, "");
        var (session, utils) = Harness.Setup(files);
        try
        {
            OpenMainFile(session, files);

            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            var srcProject = snapshot.ProjectCollection.ConfiguredProject(utils.ToPath(_myProjectRoot + "/tsconfig-src.json"));
            Assert.NotNull(srcProject);

            // Verify the default project is the source project
            var defaultProject = snapshot.GetDefaultProject(_mainUri);
            Assert.Same(srcProject, defaultProject);

            // Searched configs should be present while file is open
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect1.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Close the file and open one in an inferred project
            CloseMainFileAndOpenDummy(session);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Config files should have been released
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect1.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect2.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void WhenProjectFoundIsSolutionWithDisableReferencedProjectLoadReferencingDefaultProjectDirectly()
    {
        var files = FilesForSolutionConfigFile(["./tsconfig-src.json"], "\"disableReferencedProjectLoad\": true", null);
        var (session, utils) = Harness.Setup(files);
        try
        {
            OpenMainFile(session, files);

            // Ensure no configured project is created due to disableReferencedProjectLoad
            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.Null(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Should use inferred project instead
            var defaultProject = snapshot.GetDefaultProject(_mainUri);
            Assert.NotNull(defaultProject);
            Assert.Equal(Kind.KindInferred, defaultProject.Kind);

            // Searched configs should be present while file is open
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Close the file and open another one in the inferred project
            CloseMainFileAndOpenDummy(session);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Config files should have been released
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void WhenProjectFoundIsSolutionReferencingDefaultProjectIndirectlyThroughDisableReferencedProjectLoad()
    {
        var files = FilesForSolutionConfigFile(["./tsconfig-indirect1.json"], "", null);
        ApplyIndirectProjectFiles(files, 1, "\"disableReferencedProjectLoad\": true");
        var (session, utils) = Harness.Setup(files);
        try
        {
            OpenMainFile(session, files);

            // Ensure no configured project is created due to disableReferencedProjectLoad in indirect project
            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.Null(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Should use inferred project instead
            var defaultProject = snapshot.GetDefaultProject(_mainUri);
            Assert.NotNull(defaultProject);
            Assert.Equal(Kind.KindInferred, defaultProject.Kind);

            // Searched configs should be present while file is open
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect1.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Close the file and open another one in the inferred project
            CloseMainFileAndOpenDummy(session);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Config files should have been released
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect1.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void WhenProjectFoundIsSolutionReferencingDefaultProjectIndirectlyThroughDisableReferencedProjectLoadInOneButWithoutItInAnother()
    {
        var files = FilesForSolutionConfigFile(["./tsconfig-indirect1.json", "./tsconfig-indirect2.json"], "", null);
        ApplyIndirectProjectFiles(files, 1, "\"disableReferencedProjectLoad\": true");
        ApplyIndirectProjectFiles(files, 2, "");
        var (session, utils) = Harness.Setup(files);
        try
        {
            OpenMainFile(session, files);

            // Ensure configured project is found through the indirect project without disableReferencedProjectLoad
            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            var srcProject = snapshot.ProjectCollection.ConfiguredProject(utils.ToPath(_myProjectRoot + "/tsconfig-src.json"));
            Assert.NotNull(srcProject);

            // Verify the default project is the source project (found through indirect2, not indirect1)
            var defaultProject = snapshot.GetDefaultProject(_mainUri);
            Assert.Same(srcProject, defaultProject);

            // Searched configs should be present while file is open
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect1.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect2.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Close the file and open another one in the inferred project
            CloseMainFileAndOpenDummy(session);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Config files should have been released
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect1.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-indirect2.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void WhenProjectFoundIsProjectWithOwnFilesReferencingTheFileFromReferencedProject()
    {
        var files = FilesForSolutionConfigFile(["./tsconfig-src.json"], "", ["\"./own/main.ts\""]);
        files[_myProjectRoot + "/own/main.ts"] = """
            import { foo } from '../src/main';
            foo;
            export function bar() {}
        """;
        var (session, utils) = Harness.Setup(files);
        try
        {
            OpenMainFile(session, files);

            // Ensure configured project is found for open file - should load both projects
            var snapshot = session.Snapshot();
            Assert.Equal(2, snapshot.ProjectCollection.Projects().Length);
            var srcProject = snapshot.ProjectCollection.ConfiguredProject(utils.ToPath(_myProjectRoot + "/tsconfig-src.json"));
            Assert.NotNull(srcProject);
            var ancestorProject = snapshot.ProjectCollection.ConfiguredProject(utils.ToPath(_myProjectRoot + "/tsconfig.json"));
            Assert.NotNull(ancestorProject);

            // Verify the default project is the source project
            var defaultProject = snapshot.GetDefaultProject(_mainUri);
            Assert.Same(srcProject, defaultProject);

            // Searched configs should be present while file is open
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));

            // Close the file and open another one in the inferred project
            CloseMainFileAndOpenDummy(session);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Config files should have been released
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath(_myProjectRoot + "/tsconfig-src.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void WhenFileIsNotPartOfFirstConfigTreeFoundLooksIntoAncestorFolderAndItsReferencesToFindDefaultProject()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/src/projects/project/app/Component-demos.ts"] = """
                import * as helpers from 'demos/helpers';
                export const demo = () => {
                    helpers;
                }
            """,
            ["/home/src/projects/project/app/Component.ts"] = "export const Component = () => {}",
            ["/home/src/projects/project/app/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "composite": true,
                    "outDir": "../app-dist/",
                },
                "include": ["**/*"],
                "exclude": ["**/*-demos.*"],
            }
            """,
            ["/home/src/projects/project/demos/helpers.ts"] = "export const foo = 1;",
            ["/home/src/projects/project/demos/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "composite": true,
                    "rootDir": "../",
                    "outDir": "../demos-dist/",
                    "paths": {
                        "demos/*": ["./*"],
                    },
                },
                "include": [
                    "**/*",
                    "../app/**/*-demos.*",
                ],
            }
            """,
            ["/home/src/projects/project/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "outDir": "./dist/",
                },
                "references": [
                    { "path": "./demos/tsconfig.json" },
                    { "path": "./app/tsconfig.json" },
                ],
                "files": []
            }
            """,
        };
        var (session, utils) = Harness.Setup(files);
        try
        {
            DocumentUri uri = "file:///home/src/projects/project/app/Component-demos.ts";
            session.DidOpenFile(Context.Background, uri, 1, (string)files["/home/src/projects/project/app/Component-demos.ts"]!, LanguageKind.TypeScript);

            // Ensure configured project is found for open file
            var snapshot = session.Snapshot();
            Assert.Equal(2, snapshot.ProjectCollection.Projects().Length);
            var demoProject = snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/src/projects/project/demos/tsconfig.json"));
            Assert.NotNull(demoProject);
            var solutionProject = snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/src/projects/project/tsconfig.json"));
            Assert.NotNull(solutionProject);

            // Verify the default project is the demos project (not the app project that excludes demos files)
            var defaultProject = snapshot.GetDefaultProject(uri);
            Assert.Same(demoProject, defaultProject);

            // Searched configs should be present while file is open
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/app/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/demos/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/tsconfig.json")));

            // Close the file and open another one in the inferred project
            session.DidCloseFile(Context.Background, uri);
            session.DidOpenFile(Context.Background, _dummyUri, 1, "const x = 1;", LanguageKind.TypeScript);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Config files should have been released
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/app/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/demos/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/tsconfig.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void WhenDtsFileIsNextToTsFileAndIncludedAsRootInReferencedProject()
    {
        var files = new Dictionary<string, object?>
        {
            ["/home/src/projects/project/src/index.d.ts"] = """
                 declare global {
                    interface Window {
                        electron: ElectronAPI
                        api: unknown
                    }
                }
            """,
            ["/home/src/projects/project/src/index.ts"] = "const api = {}",
            ["/home/src/projects/project/tsconfig.json"] = """
            {
                "include": [
                    "src/*.d.ts",
                ],
                "references": [{ "path": "./tsconfig.node.json" }],
            }
            """,
            ["/home/src/projects/project/tsconfig.node.json"] = """
            {
                "include": ["src/**/*"],
                "compilerOptions": {
                    "composite": true,
                },
            }
            """,
        };
        var (session, utils) = Harness.Setup(files);
        try
        {
            DocumentUri uri = "file:///home/src/projects/project/src/index.d.ts";
            session.DidOpenFile(Context.Background, uri, 1, (string)files["/home/src/projects/project/src/index.d.ts"]!, LanguageKind.TypeScript);

            // Ensure configured projects are found for open file
            var snapshot = session.Snapshot();
            Assert.Equal(2, snapshot.ProjectCollection.Projects().Length);
            Assert.NotNull(snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/home/src/projects/project/tsconfig.json")));

            // Verify the default project is inferred
            var defaultProject = snapshot.GetDefaultProject(uri);
            Assert.NotNull(defaultProject);
            Assert.Equal(Kind.KindInferred, defaultProject.Kind);

            // Searched configs should be present while file is open
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/tsconfig.json")));
            Assert.NotNull(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/tsconfig.node.json")));

            // Close the file and open another one in the inferred project
            session.DidCloseFile(Context.Background, uri);
            session.DidOpenFile(Context.Background, _dummyUri, 1, "const x = 1;", LanguageKind.TypeScript);
            snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            Assert.NotNull(snapshot.ProjectCollection.InferredProject());

            // Config files should have been released
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/tsconfig.json")));
            Assert.Null(snapshot.ConfigFileRegistry.GetConfig(utils.ToPath("/home/src/projects/project/tsconfig.node.json")));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void Issue1630()
    {
        var files = new Dictionary<string, object?>
        {
            ["/project/lib/tsconfig.json"] = """
            {
                "files": ["a.ts"]
            }
            """,
            ["/project/lib/a.ts"] = "export const a = 1;",
            ["/project/lib/b.ts"] = "export const b = 1;",
            ["/project/tsconfig.json"] = """
            {
                "files": [],
                "references": [{ "path": "./lib" }],
                "compilerOptions": {
                    "disableReferencedProjectLoad": true
                }
            }
            """,
            ["/project/index.ts"] = "",
        };

        var (session, _) = Harness.Setup(files);
        try
        {
            // opening b.ts puts /project/lib/tsconfig.json in the config file registry and creates the project,
            // but the project is ultimately not a match
            session.DidOpenFile(Context.Background, "file:///project/lib/b.ts", 1, (string)files["/project/lib/b.ts"]!, LanguageKind.TypeScript);

            // opening an unrelated file triggers cleanup of /project/lib/tsconfig.json since no open file is part of that project,
            // but will keep the config file in the registry since lib/b.ts is still open
            session.DidOpenFile(Context.Background, "untitled:Untitled-1", 1, "", LanguageKind.TypeScript);

            // Opening index.ts searches /project/tsconfig.json and then checks /project/lib/tsconfig.json without opening it.
            // No early return on config file existence means we try to find an already open project, which returns nil,
            // triggering a crash.
            session.DidOpenFile(Context.Background, "file:///project/index.ts", 1, (string)files["/project/index.ts"]!, LanguageKind.TypeScript);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void InferredProjectRootFilesAreInStableOrder()
    {
        var files = new Dictionary<string, object?>
        {
            ["/project/a.ts"] = "export const a = 1;",
            ["/project/b.ts"] = "export const b = 1;",
            ["/project/c.ts"] = "export const c = 1;",
        };

        var (session, _) = Harness.Setup(files);
        try
        {
            // b, c, a
            session.DidOpenFile(Context.Background, "file:///project/b.ts", 1, (string)files["/project/b.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, "file:///project/c.ts", 1, (string)files["/project/c.ts"]!, LanguageKind.TypeScript);
            session.DidOpenFile(Context.Background, "file:///project/a.ts", 1, (string)files["/project/a.ts"]!, LanguageKind.TypeScript);

            var snapshot = session.Snapshot();
            Assert.Single(snapshot.ProjectCollection.Projects());
            var inferredProject = snapshot.ProjectCollection.InferredProject();
            Assert.NotNull(inferredProject);

            // It's more bookkeeping to maintain order of opening, since any file can move into or out of
            // the inferred project due to changes in other projects. Order shouldn't matter for correctness,
            // we just want it to be consistent, in case there are observable type ordering issues.
            Assert.Equal(["/project/a.ts", "/project/b.ts", "/project/c.ts"], inferredProject.Program!.CommandLine().FileNames());
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ProjectLookupTerminates()
    {
        var files = new Dictionary<string, object?>
        {
            ["/tsconfig.json"] = """
            {
                "files": [],
                "references": [
                    {
                        "path": "./packages/pkg1"
                    },
                    {
                        "path": "./packages/pkg2"
                    },
                ]
            }
            """,
            ["/packages/pkg1/tsconfig.json"] = """
            {
                "include": ["src/**/*.ts"],
                "compilerOptions": {
                    "composite": true,
                },
                "references": [
                    {
                        "path": "../pkg2"
                    },
                ]
            }
            """,
            ["/packages/pkg2/tsconfig.json"] = """
            {
                "include": ["src/**/*.ts"],
                "compilerOptions": {
                    "composite": true,
                },
                "references": [
                    {
                        "path": "../pkg1"
                    },
                ]
            }
            """,
            ["/script.ts"] = "export const a = 1;",
        };
        var (session, _) = Harness.Setup(files);
        try
        {
            session.DidOpenFile(Context.Background, "file:///script.ts", 1, (string)files["/script.ts"]!, LanguageKind.TypeScript);

            // Test should terminate
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void FileMovesToInferredProjectAfterImportIsDeleted()
    {
        // This test verifies that when a node_modules dependency file is open and its import
        // is deleted from the project root, requesting language service for the dependency
        // correctly moves it to an inferred project.
        var files = new Dictionary<string, object?>
        {
            ["/project/tsconfig.json"] = """{"compilerOptions": {"strict": true}}""",
            ["/project/index.ts"] = """import { helper } from "./node_modules/dep/index";""",
            ["/project/node_modules/dep/index.d.ts"] = "export declare function helper(): void;",
        };
        var (session, utils) = Harness.Setup(files);
        try
        {
            // Step 1: Open the project root file
            DocumentUri rootUri = "file:///project/index.ts";
            session.DidOpenFile(Context.Background, rootUri, 1, (string)files["/project/index.ts"]!, LanguageKind.TypeScript);
            ProjectTestSupport.Open(session, Context.Background, rootUri);

            // Step 2: Open the node_modules dependency file - should be in the configured project
            DocumentUri depUri = "file:///project/node_modules/dep/index.d.ts";
            session.DidOpenFile(Context.Background, depUri, 1, (string)files["/project/node_modules/dep/index.d.ts"]!, LanguageKind.TypeScript);

            var snapshot = session.Snapshot();
            var configuredProject = snapshot.ProjectCollection.ConfiguredProject(utils.ToPath("/project/tsconfig.json"));
            Assert.NotNull(configuredProject);
            var defaultProject = snapshot.GetDefaultProject(depUri);
            Assert.Same(configuredProject, defaultProject);

            // Step 3: Delete the import from the root file
            session.DidChangeFile(Context.Background, rootUri, 2,
            [
                new TextDocumentContentChangePartialOrWholeDocument
                {
                    WholeDocument = new TextDocumentContentChangeWholeDocument { Text = "// import removed" },
                },
            ]);

            // Step 4: Request language service for the dependency - it should now be in an inferred project
            var ls = ProjectTestSupport.Open(session, Context.Background, depUri);
            Assert.NotNull(ls);

            snapshot = session.Snapshot();
            defaultProject = snapshot.GetDefaultProject(depUri);
            Assert.NotNull(defaultProject);
            Assert.Equal(Kind.KindInferred, defaultProject.Kind);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void ShouldUpdateProjectOnPackageJsonChange()
    {
        // Set up a project with package.json "imports" that affect module resolution.
        // The package.json is not a program file, but it IS an affecting location.
        // When it changes, the project should be marked dirty and the program should be rebuilt.
        var packageJsonFiles = new Dictionary<string, object?>
        {
            ["/home/projects/myproject/tsconfig.json"] = """
            {
                "compilerOptions": {
                    "module": "nodenext",
                    "moduleResolution": "nodenext",
                    "noLib": true,
                    "noEmit": true
                }
            }
            """,
            ["/home/projects/myproject/package.json"] = """
            {
                "name": "myproject",
                "type": "module",
                "imports": {
                    "#utils": "./src/utils.ts"
                }
            }
            """,
            ["/home/projects/myproject/src/index.ts"] = """import { add } from "#utils";""",
            ["/home/projects/myproject/src/utils.ts"] = "export function add(a: number, b: number) { return a + b; }",
        };

        var (session, utils) = Harness.Setup(packageJsonFiles);
        try
        {
            DocumentUri indexUri = "file:///home/projects/myproject/src/index.ts";
            session.DidOpenFile(Context.Background, indexUri, 1, (string)packageJsonFiles["/home/projects/myproject/src/index.ts"]!, LanguageKind.TypeScript);

            // Verify initial state: #utils resolves to utils.ts, so utils.ts is in the program
            var ls = ProjectTestSupport.Open(session, Context.Background, indexUri);
            var program = ls.GetProgram()!;
            Assert.Empty(program.GetSemanticDiagnostics(Context.Background, null));

            // Now change the package.json to point #utils at a non-existent file
            utils.FS().WriteFile("/home/projects/myproject/package.json", """
            {
                "name": "myproject",
                "type": "module",
                "imports": {
                    "#utils": "./src/nonexistent.ts"
                }
            }
            """);
            session.DidChangeWatchedFiles(Context.Background,
            [
                new FileEvent { Uri = "file:///home/projects/myproject/package.json", Type = FileChangeType.Changed },
            ]);

            ls = ProjectTestSupport.Open(session, Context.Background, indexUri);
            var updatedProgram = ls.GetProgram()!;
            Assert.Single(updatedProgram.GetSemanticDiagnostics(Context.Background, null));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>func filesForSolutionConfigFile(solutionRefs []string, compilerOptions string, ownFiles []string)</c>。</summary>
    private static Dictionary<string, object?> FilesForSolutionConfigFile(string[] solutionRefs, string compilerOptions, string[]? ownFiles)
    {
        var compilerOptionsStr = compilerOptions.Length != 0
            ? $$"""
                			"compilerOptions": {
                				{{compilerOptions}}
                			},
                """
            : "";
        var ownFilesStr = ownFiles is null ? "" : string.Join(",", ownFiles);
        var references = string.Join(",", solutionRefs.Select(reference => $"{{ \"path\": \"{reference}\" }}"));
        return new Dictionary<string, object?>
        {
            [_myProjectRoot + "/tsconfig.json"] = $$"""
            {
            			{{compilerOptionsStr}}
            			"files": [{{ownFilesStr}}],
            			"references": [
            				{{references}}
            			]
            		}
            """,
            [_myProjectRoot + "/tsconfig-src.json"] = """
            {
            			"compilerOptions": {
            				"composite": true,
            				"outDir": "./target",
            			},
            			"include": ["./src/**/*"]
            		}
            """,
            [_myProjectRoot + "/src/main.ts"] = """
            			import { foo } from './helpers/functions';
            			export { foo };
            """,
            [_myProjectRoot + "/src/helpers/functions.ts"] = "export const foo = 1;",
        };
    }

    /// <summary>Go: <c>func applyIndirectProjectFiles(files map[string]any, projectIndex int, compilerOptions string)</c>。</summary>
    private static void ApplyIndirectProjectFiles(Dictionary<string, object?> files, int projectIndex, string compilerOptions)
    {
        foreach (var (key, value) in FilesForIndirectProject(projectIndex, compilerOptions))
        {
            files[key] = value;
        }
    }

    /// <summary>Go: <c>func filesForIndirectProject(projectIndex int, compilerOptions string)</c>。</summary>
    private static Dictionary<string, object?> FilesForIndirectProject(int projectIndex, string compilerOptions)
    {
        return new Dictionary<string, object?>
        {
            [$"{_myProjectRoot}/tsconfig-indirect{projectIndex}.json"] = $$"""
            {
            			"compilerOptions": {
            				"composite": true,
            				"outDir": "./target/",
            				{{compilerOptions}}
            			},
            			"files": [
            				"./indirect{{projectIndex}}/main.ts"
            			],
            			"references": [
            				{
            				"path": "./tsconfig-src.json"
            				}
            			]
            		}
            """,
            [$"{_myProjectRoot}/indirect{projectIndex}/main.ts"] = "export const indirect = 1;",
        };
    }
}

/// <summary>
/// 不依赖 Session 的 builder 层契约（见文件头 A/B 两部分说明）。这些用例直接驱动
/// `ProjectCollectionBuilder`/`ConfigFileRegistryBuilder`，可在 Session/LS 尚未可用时单独跑。
/// </summary>
public class ProjectCollectionBuilderLayerTests
{
    private static (ProjectCollectionBuilder Builder, ProjectCollection Collection, ConfigFileRegistry Registry) NewBuilder(
        Dictionary<string, string> files,
        string customConfigFileName = "",
        ConfigFileRegistry? baseRegistry = null,
        ProjectCollection? baseCollection = null)
    {
        var registry = baseRegistry ?? new ConfigFileRegistry();
        var toPath = ProjectBuilderTestFixture.ToPathOf("/");
        var hostFs = VfsTest.FromMap(files, useCaseSensitiveFileNames: true);
        var layered = OverlayFS.LayerOverlayFileSystem(hostFs, [], PositionEncodingKind.UTF16, toPath);
        var snapshotFs = SnapshotFSBuilder.NewSnapshotFSBuilderFromSource(layered, null, null, null, toPath);
        var collection = baseCollection ?? new ProjectCollection
        {
            _toPath = toPath,
            _configFileRegistry = registry,
            _apiState = new APIState(),
        };
        var builder = ProjectCollectionBuilder.NewProjectCollectionBuilder(
            Context.Background,
            newSnapshotID: 1,
            snapshotFs,
            [],
            collection,
            registry,
            new APIState(),
            compilerOptionsForInferredProjects: null,
            [],
            [],
            new SessionOptions { CurrentDirectory = "/" },
            customConfigFileName,
            ParseCache.NewParseCache(new RefCountCacheOptions()),
            ContentMappedParseCache.NewContentMappedParseCache(new RefCountCacheOptions()),
            ExtendedConfigCache.NewExtendedConfigCache(),
            contentMapperHost: null,
            client: null);
        return (builder, collection, registry);
    }

    [Fact]
    public void FinalizeWithoutChangesReturnsBaseCollection()
    {
        var (builder, collection, registry) = NewBuilder([]);

        var (finalizedCollection, finalizedRegistry) = builder.Finalize(null);

        Assert.Same(collection, finalizedCollection);
        Assert.Same(registry, finalizedRegistry);
    }

    [Fact]
    public void FinalizeKeepsBaseWhenNothingChangedAcrossTwoSnapshots()
    {
        var (builder, collection, _) = NewBuilder([]);
        var (first, _) = builder.Finalize(null);
        Assert.Same(collection, first);

        // 第二个 builder 建在第一个 snapshot 之上，仍然无改动 ⇒ 依然复用同一个集合实例
        var (builder2, _, _) = NewBuilder([], baseRegistry: first._configFileRegistry, baseCollection: first);
        var (second, _) = builder2.Finalize(null);
        Assert.Same(first, second);
    }

    [Fact]
    public void DidChangeCustomConfigFileNameInvalidatesDefaultProjects()
    {
        var registry = new ConfigFileRegistry { _customConfigFileName = "tsconfig.custom.json" };
        var (builder, _, _) = NewBuilder([], customConfigFileName: "tsconfig.other.json", baseRegistry: registry);
        var mainPath = new Path("/a/main.ts");
        builder._fileDefaultProjects[mainPath] = InferredProjectID.InferredProjectIDConst.AsID();

        builder.DidChangeCustomConfigFileName(null);

        // Go: b.fileDefaultProjects = nil; b.defaultProjectsInvalidated = true; b.programStructureChanged = true
        Assert.True(builder._defaultProjectsInvalidated);
        Assert.True(builder._programStructureChanged);
        Assert.Empty(builder._fileDefaultProjects);

        var (finalizedCollection, finalizedRegistry) = builder.Finalize(null);
        Assert.NotSame(registry, finalizedRegistry);
        Assert.Equal("tsconfig.other.json", finalizedRegistry._customConfigFileName);
        Assert.NotSame(registry, finalizedCollection._configFileRegistry);
    }

    [Fact]
    public void DidChangeCustomConfigFileNameWithoutRegistryChangeIsNoOp()
    {
        var registry = new ConfigFileRegistry { _customConfigFileName = "tsconfig.custom.json" };
        var (builder, _, _) = NewBuilder([], customConfigFileName: "tsconfig.custom.json", baseRegistry: registry);

        builder.DidChangeCustomConfigFileName(null);

        Assert.False(builder._defaultProjectsInvalidated);
        Assert.False(builder._programStructureChanged);
    }

    [Fact]
    public void DidRequestFileWithConfiguredProjectsOnlyDoesNotCreateInferredProject()
    {
        var (builder, _, _) = NewBuilder(new Dictionary<string, string> { ["/a/main.ts"] = "const x = 1;" });

        builder.DidRequestFile(new DocumentUri("file:///a/main.ts"), configuredProjectsOnly: true, null);

        // Go: configuredProjectsOnly 时找不到 configured project 也**不**建 inferred project
        Assert.Null(builder._inferredProject.Value());
    }

    [Fact]
    public void DidChangeFilesOnEmptySummaryIsNoOp()
    {
        var (builder, _, _) = NewBuilder([]);

        builder.DidChangeFiles(new FileChangeSummary(), null);

        Assert.False(builder._programStructureChanged);
        Assert.False(builder._openFilesChanged);
        Assert.Null(builder._inferredProject.Value());
    }

    [Fact]
    public void FinalizeReturnsBaseWhenDidChangeFilesMadeNoChanges()
    {
        var (builder, collection, registry) = NewBuilder([]);

        builder.DidChangeFiles(new FileChangeSummary(), null);
        var (finalizedCollection, finalizedRegistry) = builder.Finalize(null);

        Assert.Same(collection, finalizedCollection);
        Assert.Same(registry, finalizedRegistry);
    }
}
