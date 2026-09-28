// ported from: internal/fourslash/tests/quickInfoJsDocTagsTypedef_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTagsTypedefCase
{
    public static void TestQuickInfoJsDocTagsTypedef(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noEmit: true\n// @allowJs: true\n// @Filename: quickInfoJsDocTagsTypedef.js\n/**\n * Bar comment\n * @typedef {Object} /*1*/Bar\n * @property {string} baz - baz comment\n * @property {string} qux - qux comment\n */\n\n/**\n * foo comment\n * @param {/*2*/Bar} x - x comment\n * @returns {Bar}\n */\nfunction foo(x) {\n    return x;\n}"u8);
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
