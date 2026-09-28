// ported from: internal/fourslash/tests/inlayHintsCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class InlayHintsCrash1Case
{
    public static void TestInlayHintsCrash1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @allowJs: true\n// @checkJs: true\n// @Filename: foo.js\n/**\n * @param {function(string): boolean} f\n */\nfunction doThing(f) {\n    f(100)\n}";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyBaselineInlayHints(t, null, new LSUP.UserPreferences { InlayHints = new LSUP.InlayHintsPreferences { IncludeInlayVariableTypeHints = TRI.TSTrue, IncludeInlayParameterNameHints = LSU.IncludeInlayParameterNameHintsAll } });
            }
            finally
            {
                done();
            }
        });
    }

}
