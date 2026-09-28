// ported from: internal/fourslash/tests/formatSelectionEditAtEndOfRange_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSelectionEditAtEndOfRangeCase
{
    public static void TestFormatSelectionEditAtEndOfRange(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*1*/var x = 1;/*2*/\nvoid 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                var opts110 = f.GetOptions();
                opts110!.FormatCodeSettings!.Semicolons = "remove";
                f.Configure(t, opts110);
                f.FormatSelection(t, "1", "2");
                f.VerifyCurrentFileContent(t, "var x = 1\nvoid 0;");
            }
            finally
            {
                done();
            }
        });
    }

}
