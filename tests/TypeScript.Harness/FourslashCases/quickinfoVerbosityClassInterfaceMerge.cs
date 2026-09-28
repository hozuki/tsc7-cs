// ported from: internal/fourslash/tests/quickinfoVerbosityClassInterfaceMerge_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityClassInterfaceMergeCase
{
    public static void TestQuickinfoVerbosityClassInterfaceMerge(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ndeclare class Foo/*1*/ {\n    x: number;\n}\ndeclare interface Foo {\n    y: string;\n}\nconst f: Foo/*2*/ = { x: 1, y: \"hello\" };\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1], ["2"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
