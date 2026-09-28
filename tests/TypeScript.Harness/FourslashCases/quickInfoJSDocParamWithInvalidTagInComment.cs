// ported from: internal/fourslash/tests/quickInfoJSDocParamWithInvalidTagInComment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSDocParamWithInvalidTagInCommentCase
{
    public static void TestQuickInfoJSDocParamWithInvalidTagInComment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\n/**\n * @param {string} x Checks @-rule here\n * @param {string} a see @foo*bar here\n * @param {string} b see @test(something) here\n * @param {string} c see @*not-ident here\n * @param {string} d see @(paren) here\n */\nfunction /*fn*/foo(/**/x, /*a*/a, /*b*/b, /*c*/c, /*d*/d) {}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "fn", "function foo(x: string, a: string, b: string, c: string, d: string): void", "" + "\n\n*@param* `x` \u2014 Checks @-rule here\n" + "\n*@param* `a` \u2014 see" + "\n\n*@foo* \u2014 *bar here\n" + "\n*@param* `b` \u2014 see" + "\n\n*@test* \u2014 (something) here" + "\n\n*@param* `c` \u2014 see @*not-ident here" + "\n\n*@param* `d` \u2014 see @(paren) here");
                f.VerifyQuickInfoAt(t, "", "(parameter) x: string", "Checks @-rule here");
                f.VerifyQuickInfoAt(t, "a", "(parameter) a: string", "see");
                f.VerifyQuickInfoAt(t, "b", "(parameter) b: string", "see");
                f.VerifyQuickInfoAt(t, "c", "(parameter) c: string", "see @*not-ident here");
                f.VerifyQuickInfoAt(t, "d", "(parameter) d: string", "see @(paren) here");
            }
            finally
            {
                done();
            }
        });
    }

}
