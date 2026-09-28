// ported from: internal/fourslash/tests/getOutliningSpansForUnbalancedRegion_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansForUnbalancedRegionCase
{
    public static void TestGetOutliningSpansForUnbalancedRegion(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// top-heavy region balance\n// #region unmatched\n\n[|// #region matched\n\n// #endregion matched|]"u8);
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
