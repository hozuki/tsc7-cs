// ported from: internal/fourslash/tests/pathCompletionsAllowModuleAugmentationExtensions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PathCompletionsAllowModuleAugmentationExtensionsCase
{
    public static void TestPathCompletionsAllowModuleAugmentationExtensions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /project/foo.css\nexport const foo = 0;\n// @Filename: declarations.d.ts\ndeclare module \"*.css\" {}\n// @Filename: /project/main.ts\nimport {} from \".//**/\""u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["foo.css"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
