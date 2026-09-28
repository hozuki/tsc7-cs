// ported from: internal/fourslash/tests/quickInfoThrowsTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoThrowsTagCase
{
    public static void TestQuickInfoThrowsTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class E extends Error {}\n\n/**\n * @throws {E}\n */\nfunction f1() {}\n\n/**\n * @throws {E} description\n */\nfunction f2() {}\n\n/**\n * @throws description\n */\nfunction f3() {}\nf1/*1*/()\nf2/*2*/()\nf3/*3*/()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
