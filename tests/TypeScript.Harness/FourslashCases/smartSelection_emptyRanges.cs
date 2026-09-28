// ported from: internal/fourslash/tests/smartSelection_emptyRanges_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SmartSelection_emptyRangesCase
{
    public static void TestSmartSelection_emptyRanges(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class HomePage {\n  componentDidMount(/*1*/) {\n    if (this.props.username/*2*/) {\n      return '/*3*/';\n    }\n  }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineSelectionRanges(t);
            }
            finally
            {
                done();
            }
        });
    }

}
