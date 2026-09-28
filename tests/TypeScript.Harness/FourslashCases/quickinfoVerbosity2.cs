// ported from: internal/fourslash/tests/quickinfoVerbosity2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosity2Case
{
    public static void TestQuickinfoVerbosity2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Str = string | {};\ntype FooType = Str | number;\ntype Sym = symbol | (() => void);\ntype BarType = Sym | boolean;\ntype BothType = FooType | BarType;\nconst both/*b*/: BothType = 1;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["b"] = [0, 1, 2, 3] });
            }
            finally
            {
                done();
            }
        });
    }

}
