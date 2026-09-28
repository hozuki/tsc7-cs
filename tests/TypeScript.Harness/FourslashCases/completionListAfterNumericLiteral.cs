// ported from: internal/fourslash/tests/completionListAfterNumericLiteral_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAfterNumericLiteralCase
{
    public static void TestCompletionListAfterNumericLiteral(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: f1.ts\n0./*dotOnNumberExpressions1*/\n// @Filename: f2.ts\n0.0./*dotOnNumberExpressions2*/\n// @Filename: f3.ts\n0.0.0./*dotOnNumberExpressions3*/\n// @Filename: f4.ts\n0./** comment *//*dotOnNumberExpressions4*/\n// @Filename: f5.ts\n(0)./*validDotOnNumberExpressions1*/\n// @Filename: f6.ts\n(0.)./*validDotOnNumberExpressions2*/\n// @Filename: f7.ts\n(0.0)./*validDotOnNumberExpressions3*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["dotOnNumberExpressions1", "dotOnNumberExpressions4"] }, null!);
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["dotOnNumberExpressions2", "dotOnNumberExpressions3", "validDotOnNumberExpressions1", "validDotOnNumberExpressions2", "validDotOnNumberExpressions3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toExponential"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
