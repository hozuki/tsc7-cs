// ported from: internal/fourslash/tests/completionsPaths_pathMapping_notInNestedDirectory_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsPaths_pathMapping_notInNestedDirectoryCase
{
    public static void TestCompletionsPaths_pathMapping_notInNestedDirectory(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /user.ts\nimport {} from \"something//**/\";\n// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \".\",\n        \"paths\": {\n            \"mapping/*\": [\"whatever\"],\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [] } });
            }
            finally
            {
                done();
            }
        });
    }

}
