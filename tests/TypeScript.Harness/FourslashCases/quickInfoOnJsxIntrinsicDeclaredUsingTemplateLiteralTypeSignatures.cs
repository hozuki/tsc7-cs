// ported from: internal/fourslash/tests/quickInfoOnJsxIntrinsicDeclaredUsingTemplateLiteralTypeSignatures_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnJsxIntrinsicDeclaredUsingTemplateLiteralTypeSignaturesCase
{
    public static void TestQuickInfoOnJsxIntrinsicDeclaredUsingTemplateLiteralTypeSignatures(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @filename: /a.tsx\ndeclare namespace JSX {\n  interface IntrinsicElements {\n    [k: `foo${string}`]: any;\n    [k: `foobar${string}`]: any;\n  }\n}\n</*1*/foobaz />;\n</*2*/foobarbaz />;"u8);
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
