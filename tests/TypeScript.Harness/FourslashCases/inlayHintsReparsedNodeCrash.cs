// ported from: internal/fourslash/tests/inlayHintsReparsedNodeCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsReparsedNodeCrashCase
{
    public static void TestInlayHintsReparsedNodeCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @allowJs: true\n// @checkJs: true\n\n// @Filename: /a.js\nmodule.exports = function () {\n  return 1;\n};\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayFunctionLikeReturnTypeHints = TRI.TSTrue, IncludeInlayFunctionParameterTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
