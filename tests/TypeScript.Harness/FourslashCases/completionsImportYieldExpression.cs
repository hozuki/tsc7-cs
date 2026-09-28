// ported from: internal/fourslash/tests/completionsImportYieldExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImportYieldExpressionCase
{
    public static void TestCompletionsImportYieldExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport function a() {}\n// @Filename: /b.ts\nfunction *f() {\n  yield a/**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "a", Source = "./a", Description = "Add import from \"./a\"", NewFileContent = "import { a } from \"./a\";\n\nfunction *f() {\n  yield a\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
