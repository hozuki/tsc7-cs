// ported from: internal/fourslash/tests/importCompletions_importsMap1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportCompletions_importsMap1Case
{
    public static void TestImportCompletions_importsMap1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"lib\": [\"es5\"],\n    \"rootDir\": \"src\",\n    \"outDir\": \"dist\"\n  }\n}\n// @Filename: /home/src/workspaces/project/package.json\n{\n  \"type\": \"module\",\n  \"imports\": {\n    \"#is-browser\": {\n      \"browser\": \"./dist/env/browser.js\",\n      \"default\": \"./dist/env/node.js\"\n    }\n  }\n}\n// @Filename: /home/src/workspaces/project/src/env/browser.ts\nexport const isBrowser = true;\n// @Filename: /home/src/workspaces/project/src/env/node.ts\nexport const isBrowser = false;\n// @Filename: /home/src/workspaces/project/src/a.ts\nimport {} from \"/*1*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["#is-browser"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
