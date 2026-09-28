// ported from: internal/fourslash/tests/getOutliningSpansForUnbalancedEndRegion_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansForUnbalancedEndRegionCase
{
    public static void TestGetOutliningSpansForUnbalancedEndRegion(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// bottom-heavy region balance\n[|// #region matched\n\n// #endregion matched|]\n\n// #endregion unmatched"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyOutliningSpans(t, LSP.FoldingRangeKind.Region);
            }
            finally
            {
                done();
            }
        });
    }

}
