// ported from: internal/fourslash/tests/quickInfoJsDocTags14_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTags14Case
{
    public static void TestQuickInfoJsDocTags14(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/**\n * @param {Object} options the args object\n * @param {number} options.a first number\n * @param {number} options.b second number\n * @param {Object} options.c sub-object\n * @param {number} options.c.d third number\n * @param {Function} callback the callback function\n * @returns {number}\n */\nfunction /**/fn(options, callback = null) { }"u8);
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
