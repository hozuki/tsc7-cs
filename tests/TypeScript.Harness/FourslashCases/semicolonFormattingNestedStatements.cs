// ported from: internal/fourslash/tests/semicolonFormattingNestedStatements_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SemicolonFormattingNestedStatementsCase
{
    public static void TestSemicolonFormattingNestedStatements(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("if (true)\nif (true)/*parentOutsideBlock*/\nif (true) {\nif (true)/*directParent*/\nvar x = 0/*innermost*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "innermost");
                f.Insert(t, ";");
                f.VerifyCurrentLineContent(t, "        var x = 0;");
                f.GoToMarker(t, "directParent");
                f.VerifyCurrentLineContent(t, "    if (true)");
                f.GoToMarker(t, "parentOutsideBlock");
                f.VerifyCurrentLineContent(t, "if (true)");
            }
            finally
            {
                done();
            }
        });
    }

}
