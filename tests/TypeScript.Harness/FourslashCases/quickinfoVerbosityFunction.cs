// ported from: internal/fourslash/tests/quickinfoVerbosityFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityFunctionCase
{
    public static void TestQuickinfoVerbosityFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Apple {\n    color: string;\n    size: number;\n}\ninterface Orchard {\n    takeOneApple(a: Apple): void;\n    getApple(): Apple;\n    getApple(size: number): Apple[];\n}\nconst o/*o*/: Orchard = {} as any;\ndeclare function isApple/*f*/(x: unknown): x is Apple;\ntype SomeType = {\n    prop1: string;\n}\nfunction someFun(a: SomeType): SomeType {\n    return a;\n}\nsomeFun/*s*/.what = 'what';"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["o"] = [0, 1, 2], ["f"] = [0, 1], ["s"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
