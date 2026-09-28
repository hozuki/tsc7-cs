// ported from: internal/fourslash/tests/quickInfoJsDocTags1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTags1Case
{
    public static void TestQuickInfoJsDocTags1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: quickInfoJsDocTags1.ts\n/**\n * Doc\n * @author Me <me@domain.tld>\n * @augments {C<T>} Augments it\n * @template T A template\n * @type {number | string} A type\n * @typedef {number | string} NumOrStr\n * @property {number} x The prop\n * @param {number} x The param\n * @returns The result\n * @see x (the parameter)\n */\nfunction /**/foo(x) {}"u8);
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
