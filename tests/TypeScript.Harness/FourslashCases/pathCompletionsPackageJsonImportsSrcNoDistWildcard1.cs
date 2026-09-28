// ported from: internal/fourslash/tests/pathCompletionsPackageJsonImportsSrcNoDistWildcard1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsPackageJsonImportsSrcNoDistWildcard1Case
{
    public static void TestPathCompletionsPackageJsonImportsSrcNoDistWildcard1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"rootDir\": \"src\",\n    \"outDir\": \"dist\"\n  }\n}\n// @Filename: /home/src/workspaces/project/package.json\n{\n  \"name\": \"foo\",\n  \"main\": \"dist/index.js\",\n  \"module\": \"dist/index.mjs\",\n  \"types\": \"dist/index.d.ts\",\n  \"imports\": {\n    \"#*\": {\n      \"types\": \"./dist/*.d.ts\",\n      \"import\": \"./dist/*.mjs\",\n      \"default\": \"./dist/*.js\"\n    },\n    \"#arguments\": {\n      \"types\": \"./dist/arguments/index.d.ts\",\n      \"import\": \"./dist/arguments/index.mjs\",\n      \"default\": \"./dist/arguments/index.js\"\n    }\n  }\n}\n// @Filename: /home/src/workspaces/project/src/index.ts\nexport const index = 0;\n// @Filename: /home/src/workspaces/project/src/blah.ts\nexport const blah = 0;\n// @Filename: /home/src/workspaces/project/src/arguments/index.ts\nexport const arguments = 0;\n// @Filename: /home/src/workspaces/project/src/m.mts\nimport { } from \"/**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "#blah", Kind = LSP.CompletionItemKind.File, Detail = "#blah.ts" }, new LSP.CompletionItem { Label = "#index", Kind = LSP.CompletionItemKind.File, Detail = "#index.ts" }, new LSP.CompletionItem { Label = "#arguments", Kind = LSP.CompletionItemKind.File, Detail = "#arguments.d.ts" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
