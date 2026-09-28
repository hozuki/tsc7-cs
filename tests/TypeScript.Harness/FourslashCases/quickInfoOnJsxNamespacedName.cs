// ported from: internal/fourslash/tests/quickInfoOnJsxNamespacedName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnJsxNamespacedNameCase
{
    public static void TestQuickInfoOnJsxNamespacedName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @Filename: /types.d.ts\ndeclare namespace JSX {\n    interface IntrinsicElements { ['a:b']: { a: string }; }\n}\n// @filename: /a.tsx\n</**/a:b a=\"accepted\" b=\"rejected\" />;"u8);
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
