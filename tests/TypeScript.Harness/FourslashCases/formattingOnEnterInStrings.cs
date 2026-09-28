// ported from: internal/fourslash/tests/formattingOnEnterInStrings_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormattingOnEnterInStringsCase
{
    public static void TestFormattingOnEnterInStrings(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = /*1*/\"unclosed string literal\\/*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "2");
                f.InsertLine(t, "");
                f.InsertLine(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "var x = \"unclosed string literal\\");
            }
            finally
            {
                done();
            }
        });
    }

}
