// ported from: internal/fourslash/tests/inlayHintsFunctionParameterTypes4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsFunctionParameterTypes4Case
{
    public static void TestInlayHintsFunctionParameterTypes4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @Filename: /a.js\nclass Foo {\n    #value = 0;\n    get foo() { return this.#value; }\n    /**\n     * @param {number} value\n     */\n    set foo(value) { this.#value = value; }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null!, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayFunctionParameterTypeHints = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
