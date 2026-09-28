// ported from: internal/fourslash/tests/quickinfoVerbosityConditionalType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityConditionalTypeCase
{
    public static void TestQuickinfoVerbosityConditionalType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Apple {\n    color: string;\n    weight: number;\n}\ntype StrInt = string | bigint;\ntype T1<T extends Apple | Apple[]> = T extends { color: string } ? \"one apple\" : StrInt;\nfunction f<T extends Apple | Apple[]>(x: T1<T>): void {\n    x/*x*/;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["x"] = [0, 1, 2] });
            }
            finally
            {
                done();
            }
        });
    }

}
