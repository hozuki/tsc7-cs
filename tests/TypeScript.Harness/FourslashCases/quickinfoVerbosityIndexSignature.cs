// ported from: internal/fourslash/tests/quickinfoVerbosityIndexSignature_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityIndexSignatureCase
{
    public static void TestQuickinfoVerbosityIndexSignature(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Key = string | number;\ninterface Apple {\n    banana: number;\n}\ninterface Foo {\n    [a/*a*/: Key]: Apple;\n}\nconst f/*f*/: Foo = {};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["a"] = [0, 1], ["f"] = [0, 1, 2] });
            }
            finally
            {
                done();
            }
        });
    }

}
