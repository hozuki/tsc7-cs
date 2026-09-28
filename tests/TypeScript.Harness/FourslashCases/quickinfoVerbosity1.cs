// ported from: internal/fourslash/tests/quickinfoVerbosity1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosity1Case
{
    public static void TestQuickinfoVerbosity1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type FooType = string | number;\nconst foo/*a*/: FooType = 1;\ntype BarType = FooType | boolean;\nconst bar/*b*/: BarType = 1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["a"] = [0, 1], ["b"] = [0, 1, 2] });
            }
            finally
            {
                done();
            }
        });
    }

}
