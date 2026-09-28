// ported from: internal/fourslash/tests/quickinfoVerbosityNamespaceMergedInterfaceHeritage_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityNamespaceMergedInterfaceHeritageCase
{
    public static void TestQuickinfoVerbosityNamespaceMergedInterfaceHeritage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ndeclare namespace NS/*1*/ {\n    interface Config extends A {\n        a: string;\n    }\n\n    interface Config extends B {\n        b: number;\n    }\n\n    interface A {\n        a: string;\n    }\n\n    interface B {\n        b: number;\n    }\n}\n"u8);
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
