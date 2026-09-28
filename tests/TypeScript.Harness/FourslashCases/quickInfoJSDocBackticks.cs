// ported from: internal/fourslash/tests/quickInfoJSDocBackticks_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSDocBackticksCase
{
    public static void TestQuickInfoJSDocBackticks(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noEmit: true\n// @allowJs: true\n// @checkJs: true\n// @strict: true\n// @Filename: jsdocParseMatchingBackticks.js\n/**\n * `@param` initial at-param is OK in title comment\n * @param {string} x hi there `@param`\n * @param {string} y hi there `@ * param\n *                   this is the margin\n */\nexport function f(x, y) {\n    return x/*x*/ + y/*y*/\n}\nf/*f*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "f");
                f.VerifyQuickInfoIs(t, "function f(x: string, y: string): string", "`@param` initial at-param is OK in title comment");
                f.GoToMarker(t, "x");
                f.VerifyQuickInfoIs(t, "(parameter) x: string", "hi there `@param`");
                f.GoToMarker(t, "y");
                f.VerifyQuickInfoIs(t, "(parameter) y: string", "hi there `@ * param\nthis is the margin");
            }
            finally
            {
                done();
            }
        });
    }

}
