// ported from: internal/fourslash/tests/jsdocLink2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocLink2Case
{
    public static void TestJsdocLink2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: jsdocLink2.ts\nclass C {\n}\n// @Filename: script.ts\n/**\n * {@link C}\n * @wat Makes a {@link C}. A default one.\n * {@link C()}\n * {@link C|postfix text}\n * {@link unformatted postfix text}\n * @see {@link C} its great\n */\nfunction /**/CC() {\n}"u8);
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
