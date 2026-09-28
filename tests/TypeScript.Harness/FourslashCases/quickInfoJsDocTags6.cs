// ported from: internal/fourslash/tests/quickInfoJsDocTags6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTags6Case
{
    public static void TestQuickInfoJsDocTags6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noEmit: true\n// @allowJs: true\n// @Filename: quickInfoJsDocTags6.js\nclass Foo {\n    /**\n     * comment\n     * @author Me <me@domain.tld>\n     * @see x (the parameter)\n     * @param {number} x - x comment\n     * @param {number} y - y comment\n     * @returns The result\n     */\n    method(x, y) {\n       return x + y;\n    }\n}\n\nclass Bar extends Foo {\n    /** @inheritDoc */\n    /**/method(x, y) {\n        const res = super.method(x, y) + 100;\n        return res;\n    }\n}"u8);
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
