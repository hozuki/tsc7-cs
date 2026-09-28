// ported from: internal/fourslash/tests/inlayHintsQuotePreference2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsQuotePreference2Case
{
    public static void TestInlayHintsQuotePreference2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const a1: \"'\" = \"'\";\nconst b1: \"\\\\\" = \"\\\\\";\nexport function fn(a = a1, b = b1) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { QuotePreference = new LSUP.QuotePreference("single"), InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayFunctionParameterTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
