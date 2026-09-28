// ported from: internal/fourslash/tests/importCompletionsPackageJsonExportsTrailingSlash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportCompletionsPackageJsonExportsTrailingSlash1Case
{
    public static void TestImportCompletionsPackageJsonExportsTrailingSlash1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @moduleResolution: nodenext\n// @Filename: /node_modules/pkg/package.json\n{\n    \"name\": \"pkg\",\n    \"version\": \"1.0.0\",\n    \"exports\": {\n      \"./test/\": \"./\"\n    }\n }\n// @Filename: /node_modules/pkg/foo.d.ts\nexport function foo(): void;\n// @Filename: /package.json\n{\n    \"dependencies\": {\n       \"pkg\": \"*\"\n    }\n }\n// @Filename: /index.ts\nimport {} from \"pkg//*1*/\";\nimport {} from \"pkg/test//*2*/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["test"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["foo.js"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
