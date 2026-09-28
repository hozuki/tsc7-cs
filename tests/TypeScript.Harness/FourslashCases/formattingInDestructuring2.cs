// ported from: internal/fourslash/tests/formattingInDestructuring2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingInDestructuring2Case
{
    public static void TestFormattingInDestructuring2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/function   drawText(    { text = \"\", location: [x, y]=           [0, 0], bold = false }) {\n    // Draw text  \n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "function drawText({ text = \"\", location: [x, y] = [0, 0], bold = false }) {");
            }
            finally
            {
                done();
            }
        });
    }

}
