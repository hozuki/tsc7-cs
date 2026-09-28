// ported from: internal/fourslash/tests/quickinfoVerbosityTuple_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityTupleCase
{
    public static void TestQuickinfoVerbosityTuple(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Orange {\n    color: string;\n}\ninterface Apple {\n    color: string;\n    other: Orange;\n}\ntype TwoFruits/*T*/ = [Orange, Apple];\nconst tf/*f*/: TwoFruits = [\n    { color: \"orange\" },\n    { color: \"red\", other: { color: \"orange\" } }\n];\nconst tf2/*f2*/: [Orange, Apple] = [\n    { color: \"orange\" },\n    { color: \"red\", other: { color: \"orange\" } }\n];\ntype ManyFruits/*m*/ = (Orange | Apple)[];\nconst mf/*mf*/: ManyFruits = [];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["T"] = [0, 1, 2], ["f"] = [0, 1, 2, 3], ["f2"] = [0, 1, 2], ["m"] = [0, 1, 2], ["mf"] = [0, 1, 2, 3] });
            }
            finally
            {
                done();
            }
        });
    }

}
