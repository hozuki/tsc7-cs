// ported from: internal/fourslash/tests/quickInfoLink3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoLink3Case
{
    public static void TestQuickInfoLink3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo<T> {\n    /**\n     * {@link Foo}\n     * {@link Foo<T>}\n     * {@link Foo<Array<X>>}\n     * {@link Foo<>}\n     * {@link Foo>}\n     * {@link Foo<}\n     */\n    bar/**/(){}\n}"u8);
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
