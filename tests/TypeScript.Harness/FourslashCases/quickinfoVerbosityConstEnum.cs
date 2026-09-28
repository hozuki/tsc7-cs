// ported from: internal/fourslash/tests/quickinfoVerbosityConstEnum_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityConstEnumCase
{
    public static void TestQuickinfoVerbosityConstEnum(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nconst enum Direction/*1*/ {\n    Up = \"UP\",\n    Down = \"DOWN\",\n    Left = \"LEFT\",\n    Right = \"RIGHT\",\n}\n\nenum NumericEnum/*2*/ {\n    A,\n    B = 10,\n    C,\n}\n"u8);
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
