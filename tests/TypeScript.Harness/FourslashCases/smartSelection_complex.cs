// ported from: internal/fourslash/tests/smartSelection_complex_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SmartSelection_complexCase
{
    public static void TestSmartSelection_complex(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type X<T, P> = IsExactlyAny<P> extends true ? T : ({ [K in keyof P]: IsExactlyAny<P[K]> extends true ? K extends keyof T ? T[K] : P[/**/K] : P[K]; } & Pick<T, Exclude<keyof T, keyof P>>)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineSelectionRanges(t);
            }
            finally
            {
                done();
            }
        });
    }

}
