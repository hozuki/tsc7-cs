// ported from: internal/fourslash/tests/formattingMultilineCommentsWithTabs1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingMultilineCommentsWithTabs1Case
{
    public static void TestFormattingMultilineCommentsWithTabs1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var f = function (j) {\n\n\tswitch (j) {\n\t\tcase 1:\n/*1*/\t\t\t\t/* when current checkbox has focus, Firefox has changed check state already\n/*2*/\t\t\t\ton SPACE bar press only\n/*3*/\t\t\t\tIE does not have issue, use the CSS class\n/*4*/\t\t\t\tinput:focus[type=checkbox] (z-index = 31290)\n/*5*/\t\t\t\tto determine whether checkbox has focus or not\n\t\t\t\t*/\n\t\t\tbreak;\n\t\tcase 2:\n\t\tbreak;\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "            /* when current checkbox has focus, Firefox has changed check state already");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "            on SPACE bar press only");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "            IE does not have issue, use the CSS class");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "            input:focus[type=checkbox] (z-index = 31290)");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "            to determine whether checkbox has focus or not");
            }
            finally
            {
                done();
            }
        });
    }

}
