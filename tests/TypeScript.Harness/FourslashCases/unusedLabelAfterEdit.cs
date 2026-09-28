// ported from: internal/fourslash/tests/unusedLabelAfterEdit_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedLabelAfterEditCase
{
    public static void TestUnusedLabelAfterEdit(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowUnusedLabels: false\nmyLabel: while (true) {\n    if (Math.random() > 0.5) {\n        /*marker*/break myLabel;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
                f.GoToMarker(t, "marker");
                f.DeleteAtCaret(t, 14);
                f.Insert(t, "break;");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
                f.GoToMarker(t, "marker");
                f.DeleteAtCaret(t, 6);
                f.Insert(t, "break myLabel;");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
