// ported from: internal/fourslash/tests/formatWithStatement_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatWithStatementCase
{
    public static void TestFormatWithStatement(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("with /*1*/(foo.bar)\n\n   {/*2*/\n\n     }/*3*/\n\nwith (bar.blah)/*4*/\n{/*5*/\n}/*6*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts227 = f.GetOptions();
                opts227!.FormatCodeSettings!.PlaceOpenBraceOnNewLineForControlBlocks = TRI.TSFalse;
                f.Configure(t, opts227);
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "with (foo.bar) {");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "}");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "with (bar.blah) {");
                f.GoToMarker(t, "6");
                f.VerifyCurrentLineContent(t, "}");
                var opts565 = f.GetOptions();
                opts565!.FormatCodeSettings!.PlaceOpenBraceOnNewLineForControlBlocks = TRI.TSTrue;
                f.Configure(t, opts565);
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "with (foo.bar)");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "{");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "with (bar.blah)");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "{");
            }
            finally
            {
                done();
            }
        });
    }

}
