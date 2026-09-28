// ported from: internal/fourslash/tests/formattingOnChainedCallbacksAndPropertyAccesses_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnChainedCallbacksAndPropertyAccessesCase
{
    public static void TestFormattingOnChainedCallbacksAndPropertyAccesses(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = 1;\nx\n/*1*/.toFixed\nx\n/*2*/.toFixed()\nx\n/*3*/.toFixed()\n/*4*/.length\n/*5*/.toString();\nx\n/*6*/.toFixed\n/*7*/.toString()\n/*8*/.length;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "    .toFixed");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "    .toFixed()");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "    .toFixed()");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "    .length");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "    .toString();");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "    .toFixed");
                f.GoToMarker(t, "7");
                f.VerifyCurrentLineContent(t, "    .toString()");
                f.GoToMarker(t, "8");
                f.VerifyCurrentLineContent(t, "    .length;");
            }
            finally
            {
                done();
            }
        });
    }

}
