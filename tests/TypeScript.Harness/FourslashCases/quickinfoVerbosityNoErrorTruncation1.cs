// ported from: internal/fourslash/tests/quickinfoVerbosityNoErrorTruncation1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityNoErrorTruncation1Case
{
    public static void TestQuickinfoVerbosityNoErrorTruncation1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noErrorTruncation: true\ntype /*1*/T = [\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 0,\n  'still good', 'now truncating'\n];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
