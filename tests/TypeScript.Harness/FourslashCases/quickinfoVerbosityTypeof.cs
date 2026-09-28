// ported from: internal/fourslash/tests/quickinfoVerbosityTypeof_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityTypeofCase
{
    public static void TestQuickinfoVerbosityTypeof(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Apple {\n    color: string;\n    weight: number;\n}\nconst a: Apple = { color: \"red\", weight: 150 };\nconst b/*b*/: typeof a = { color: \"green\", weight: 120 };\nclass Banana {\n    length: number;\n    constructor(length: number) {\n        this.length = length;\n    }\n}\nconst c/*c*/: typeof Banana = Banana;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["b"] = [0, 1], ["c"] = [0, 1] });
            }
            finally
            {
                done();
            }
        });
    }

}
