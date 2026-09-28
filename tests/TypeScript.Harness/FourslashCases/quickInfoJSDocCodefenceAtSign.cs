// ported from: internal/fourslash/tests/quickInfoJSDocCodefenceAtSign_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSDocCodefenceAtSignCase
{
    public static void TestQuickInfoJSDocCodefenceAtSign(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/**\n * text\n * @example Foo\n * ```\n * @Embed[asfasdfasf]\n * ```\n * becomes\n * ```html\n * <div></div>\n * ```\n */\nconst /*1*/x = 1;\n\n/**\n * Some text\n * ```\n * @tag inside code\n * ```\n * @param y - a number\n */\nfunction /*2*/foo(y: number) {}\n"u8);
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
