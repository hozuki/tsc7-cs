// ported from: internal/fourslash/tests/privatePropertyOfUndefinedThis_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PrivatePropertyOfUndefinedThisCase
{
    public static void TestPrivatePropertyOfUndefinedThis1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @strict: true\nthis.#a = {};\nexport {};\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineNonSuggestionDiagnostics(t);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestPrivatePropertyOfUndefinedThis2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @strict: true\nexport class C {\n    wat(this: any): boolean {\n        return this.#prop = {};\n    }\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineNonSuggestionDiagnostics(t);
            }
            finally
            {
                done();
            }
        });
    }

}
