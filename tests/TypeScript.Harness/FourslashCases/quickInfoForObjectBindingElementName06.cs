// ported from: internal/fourslash/tests/quickInfoForObjectBindingElementName06_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForObjectBindingElementName06Case
{
    public static void TestQuickInfoForObjectBindingElementName06(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type Foo = {\n    /**\n     * Thing is a bar\n     */\n    isBar: boolean\n\n    /**\n     * Thing is a baz\n     */\n    isBaz: boolean\n}\n\nfunction f(): Foo {\n    return undefined as any\n}\n\nconst { isBaz: isBar } = f();\nisBar/**/;"u8);
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
