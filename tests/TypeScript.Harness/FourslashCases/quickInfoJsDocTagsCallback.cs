// ported from: internal/fourslash/tests/quickInfoJsDocTagsCallback_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTagsCallbackCase
{
    public static void TestQuickInfoJsDocTagsCallback(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noEmit: true\n// @allowJs: true\n// @Filename: quickInfoJsDocTagsCallback.js\n/**\n * @callback cb/*1*/\n * @param {string} x - x comment\n */\n\n/**\n * @param {/*2*/cb} bar -callback comment\n */\nfunction foo(bar) {\n    bar(bar);\n}"u8);
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
