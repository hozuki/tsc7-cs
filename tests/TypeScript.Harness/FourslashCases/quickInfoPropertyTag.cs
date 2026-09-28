// ported from: internal/fourslash/tests/quickInfoPropertyTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoPropertyTagCase
{
    public static void TestQuickInfoPropertyTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\n/**\n * @typedef I\n * @property {number} x Doc\n *                      More doc\n */\n\n/** @type {I} */\nconst obj = { /**/x: 10 };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(property) x: number", "Doc\nMore doc");
            }
            finally
            {
                done();
            }
        });
    }

}
