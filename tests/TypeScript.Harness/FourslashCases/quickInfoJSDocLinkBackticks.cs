// ported from: internal/fourslash/tests/quickInfoJSDocLinkBackticks_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSDocLinkBackticksCase
{
    public static void TestQuickInfoJSDocLinkBackticks(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noEmit: true\n// @allowJs: true\n// @checkJs: true\n// @strict: true\n// @Filename: jsdocParseMatchingBackticks.js\n/**\n * `{@link foo}` initial at-param is OK in title comment\n * @param {string} x hi there `{@link foo}`\n */\nexport function f(x) {\n    return x/*x*/\n}\nf/*f*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "f");
                f.VerifyQuickInfoIs(t, "function f(x: string): string", "`{@link foo}` initial at-param is OK in title comment\n\n*@param* `x` \u2014 hi there `{@link foo}`");
                f.GoToMarker(t, "x");
                f.VerifyQuickInfoIs(t, "(parameter) x: string", "hi there `{@link foo}`");
            }
            finally
            {
                done();
            }
        });
    }

}
