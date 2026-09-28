// ported from: internal/fourslash/tests/quickInfoInheritDoc3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoInheritDoc3Case
{
    public static void TestQuickInfoInheritDoc3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noEmit: true\n// @allowJs: true\n// @Filename: quickInfoInheritDoc3.ts\nfunction getBaseClass() {\n    return class Base {\n        /**\n         * Base.prop\n         */\n        prop: string | undefined;\n    }\n}\nclass SubClass extends getBaseClass() {\n    /**\n     * @inheritdoc\n     * SubClass.prop\n     */\n    /*1*/prop: string | undefined;\n}"u8);
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
