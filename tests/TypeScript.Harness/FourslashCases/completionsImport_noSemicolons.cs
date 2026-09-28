// ported from: internal/fourslash/tests/completionsImport_noSemicolons_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_noSemicolonsCase
{
    public static void TestCompletionsImport_noSemicolons(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport function foo() {}\n// @Filename: /b.ts\nconst x = 0\nconst y = 1\nconst z = fo/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "foo", Source = "./a", Description = "Add import from \"./a\"", NewFileContent = "import { foo } from \"./a\"\n\nconst x = 0\nconst y = 1\nconst z = fo" });
            }
            finally
            {
                done();
            }
        });
    }

}
