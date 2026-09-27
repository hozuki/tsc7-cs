// ported from: internal/project/projectcollectiondefaultproject_test.go:1-129
//
// 与 Go 测试的差异（逐条）：
// 1) `projecttestutil.Setup(files)` → `ProjectTestUtil.Setup(files)`；`bundled.Embedded` 跳过分支不移植（恒为 true）。
// 2) `content := files[string(uri)[7:]].(string)`（去掉 "file://" 前缀）→ C# `(string)files[uri.Value[7..]]!`。
// 3) `[]lsproto.DocumentUri{…}` → C# 集合表达式 `[ … ]`（元素是字符串 → DocumentUri 的隐式转换）。
// 4) 该用例只断言"不崩"（`// Should not crash.`），故除调用外没有断言；`session.Close()` 放 finally。
using System.Collections.Generic;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using Harness = TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;

namespace TypeScript.Tests;

public class ProjectCollectionDefaultProjectTests
{
    // Project 1 references project 2, which does not have open files.
    // File project1/dist/index.d.ts does not belong to any tsconfig.json, but is included in programs for
    // projects 3 and 4 via project 1's output.
    // When looking for a default project for project1/dist/index.d.ts,
    // we should not try to unconditionally access project 2,
    // which isn't loaded because of `disableReferencedProjectLoad`.
    private static Dictionary<string, object?> Files() => new()
    {
        ["/project1/tsconfig.json"] = """
        {
            "extends": "../tsconfig.json",
            "files": [],
            "include": ["src/**/*"],
            "references": [
                {
                    "path": "../project2"
                }
            ],
            "compilerOptions": {
                "composite": true,
                "outDir": "./dist",
                "rootDir": "./src",
            }
        }
        """,
        ["/project1/src/index.ts"] = """
        export const foo = 42;
        export type Bar = { a: string };
        """,
        ["/project1/dist/index.d.ts"] = """
        export declare const foo = 42;
        export type Bar = {
            a: string;
        };
        """,
        ["/project2/tsconfig.json"] = """
        {
            "extends": "../tsconfig.json",
            "files": [],
            "include": ["src/**/*"],
            "compilerOptions": {
                "composite": true,
                "outDir": "./dist",
                "rootDir": "./src"
            }
        }
        """,
        ["/project3/tsconfig.json"] = """
        {
            "extends": "../tsconfig.json",
            "files": [],
            "include": ["src/**/*"],
            "references": [
                {
                    "path": "../project1"
                }
            ],
            "compilerOptions": {
                "composite": true,
                "outDir": "./dist",
                "rootDir": "./src",
            }
        }
        """,
        ["/project3/src/index.ts"] = """
        import { Bar } from "../../project1/dist/index.js";
        declare const b: Bar;
        const x: string = b.a;
        """,
        ["/project4/tsconfig.json"] = """
        {
            "extends": "../tsconfig.json",
            "files": [],
            "include": ["src/**/*"],
            "references": [
                {
                    "path": "../project1"
                }
            ],
            "compilerOptions": {
                "composite": true,
                "outDir": "./dist",
                "rootDir": "./src",
            }
        }
        """,
        ["/project4/src/index.ts"] = """
        import { Bar } from "../../project1/dist/index.js";
        declare const b: Bar;
        const x: string = b.a;
        """,
        ["/tsconfig.json"] = """
        {
            "compilerOptions": {
                "disableReferencedProjectLoad": true,
                "disableSolutionSearching": true,
                "disableSourceOfProjectReferenceRedirect": true
            },
            "files": [],
            "references": [
                {
                    "path": "./project1"
                },
                {
                    "path": "./project2"
                },
                {
                    "path": "./project3"
                },
                {
                    "path": "./project4"
                }
            ]
        }
        """,
    };

    [Fact]
    public void ShouldNotCrashWhenLookingUpDefaultProjectForReferencedOutput()
    {
        var files = Files();
        DocumentUri[] uris =
        [
            "file:///project1/dist/index.d.ts",
            "file:///project1/src/index.ts",
            "file:///project3/src/index.ts",
            "file:///project4/src/index.ts",
        ];
        var (session, _) = Harness.Setup(files);
        try
        {
            // Should not crash.
            foreach (var uri in uris)
            {
                var content = (string)files[uri.Value[7..]]!;
                session.DidOpenFile(Context.Background, uri, 1, content, LanguageKind.TypeScript);
            }
        }
        finally
        {
            session.Close();
        }
    }
}
