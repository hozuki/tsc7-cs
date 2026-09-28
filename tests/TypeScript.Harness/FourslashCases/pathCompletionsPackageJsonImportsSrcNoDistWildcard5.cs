// ported from: internal/fourslash/tests/pathCompletionsPackageJsonImportsSrcNoDistWildcard5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonImportsSrcNoDistWildcard5Case
{
    public static void TestPathCompletionsPackageJsonImportsSrcNoDistWildcard5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"rootDir\": \"src\",\n    \"outDir\": \"dist/esm\",\n    \"declarationDir\": \"dist/types\"\n  }\n}\n// @Filename: /home/src/workspaces/project/package.json\n{\n  \"name\": \"foo\",\n  \"main\": \"dist/index.js\",\n  \"module\": \"dist/index.mjs\",\n  \"types\": \"dist/index.d.ts\",\n  \"imports\": {\n    \"#*\": {\n      \"import\": {\n        \"types\": \"./dist/types/*.d.mts\",\n        \"default\": \"./dist/esm/*.mjs\"\n      },\n      \"default\": {\n        \"types\": \"./dist/types/*.d.ts\",\n        \"default\": \"./dist/cjs/*.js\"\n      }\n    },\n    \"#only-in-cjs\": {\n      \"require\": {\n        \"types\": \"./dist/types/only-in-cjs/index.d.ts\",\n        \"default\": \"./dist/cjs/only-in-cjs/index.js\"\n      }\n    }\n  }\n}\n// @Filename: /home/src/workspaces/project/src/index.mts\nexport const index = 0;\n// @Filename: /home/src/workspaces/project/src/index.ts\nexport const index = 0;\n// @Filename: /home/src/workspaces/project/src/blah.mts\nexport const blah = 0;\n// @Filename: /home/src/workspaces/project/src/blah.ts\nexport const blah = 0;\n// @Filename: /home/src/workspaces/project/src/only-in-cjs/index.ts\nexport const onlyInCjs = 0;\n// @Filename: /home/src/workspaces/project/src/index.mts\nimport { } from \"/**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "#blah", Kind = LSP.CompletionItemKind.File, Detail = "#blah.mts" }, new LSP.CompletionItem { Label = "#index", Kind = LSP.CompletionItemKind.File, Detail = "#index.mts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
