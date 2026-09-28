// ported from: internal/fourslash/tests/smartSelection_mappedTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SmartSelection_mappedTypesCase
{
    public static void TestSmartSelection_mappedTypes(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type M = { /*1*/-re/*2*/adonly /*3*/[K in ke/*4*/yof any]/*5*/-/*6*/?: any };"u8);
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
