// ported from: internal/fourslash/tests/formattingNonNullAssertionOperator_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingNonNullAssertionOperatorCase
{
    public static void TestFormattingNonNullAssertionOperator(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/ 'bar' ! ;\n/*2*/ ( 'bar' ) ! ;\n/*3*/ 'bar' [ 1 ] ! ;\n/*4*/ var  bar  =  'bar' . foo ! ;\n/*5*/ var  foo  =  bar ! ;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "'bar'!;");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "('bar')!;");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "'bar'[1]!;");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "var bar = 'bar'.foo!;");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "var foo = bar!;");
            }
            finally
            {
                done();
            }
        });
    }

}
