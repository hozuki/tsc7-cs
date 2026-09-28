// ported from: internal/fourslash/tests/formatDotAfterNumber_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatDotAfterNumberCase
{
    public static void TestFormatDotAfterNumber(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("1+ 2 .toString() +3/*1*/\n1+ 2. .toString() +3/*2*/\n1+ 2.0 .toString() +3/*3*/\n1+ (2) .toString() +3/*4*/\n1+ 2_000 .toString() +3/*5*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatDocument(t, "");
                f.GoToMarker(t, "1");
                f.VerifyCurrentLineContent(t, "1 + 2 .toString() + 3");
                f.GoToMarker(t, "2");
                f.VerifyCurrentLineContent(t, "1 + 2..toString() + 3");
                f.GoToMarker(t, "3");
                f.VerifyCurrentLineContent(t, "1 + 2.0.toString() + 3");
                f.GoToMarker(t, "4");
                f.VerifyCurrentLineContent(t, "1 + (2).toString() + 3");
                f.GoToMarker(t, "5");
                f.VerifyCurrentLineContent(t, "1 + 2_000 .toString() + 3");
            }
            finally
            {
                done();
            }
        });
    }

}
