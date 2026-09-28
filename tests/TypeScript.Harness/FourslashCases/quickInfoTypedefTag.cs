// ported from: internal/fourslash/tests/quickInfoTypedefTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoTypedefTagCase
{
    public static void TestQuickInfoTypedefTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\n/**\n * The typedef tag should not appear in the quickinfo.\n * @typedef {{ foo: 'foo' }} Foo\n */\nfunction f() { }\nf/*1*/()\n/**\n * A removed comment\n * @tag Usage shows that non-param tags in comments explain the typedef instead of using it\n * @typedef {{ nope: any }} Nope not here\n * @tag comment 2\n */\nfunction g() { }\ng/*2*/()\n/**\n * The whole thing is kept\n * @param {Local} keep\n * @typedef {{ local: any }} Local kept too\n * @returns {void} also kept\n */\nfunction h(keep) { }\nh/*3*/({ nope: 1 })"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
