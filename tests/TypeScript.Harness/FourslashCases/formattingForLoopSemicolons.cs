// ported from: internal/fourslash/tests/formattingForLoopSemicolons_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingForLoopSemicolonsCase
{
    public static void TestFormattingForLoopSemicolons(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/for (;;) { }\n/*2*/for (var x;x<0;x++) { }\n/*3*/for (var x ;x<0 ;x++) { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "for (; ;) { }");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "for (var x; x < 0; x++) { }");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "for (var x; x < 0; x++) { }");
                var opts444 = f.GetOptions();
                opts444!.FormatCodeSettings!.InsertSpaceAfterSemicolonInForStatements = TRI.TSFalse;
                f.Configure(t, opts444);
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "for (;;) { }");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "for (var x;x < 0;x++) { }");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "for (var x;x < 0;x++) { }");
            }
            finally
            {
                done();
            }
        });
    }

}
