// ported from: internal/fourslash/tests/quickInfoOnJsxIntrinsicDeclaredUsingCatchCallIndexSignature_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnJsxIntrinsicDeclaredUsingCatchCallIndexSignatureCase
{
    public static void TestQuickInfoOnJsxIntrinsicDeclaredUsingCatchCallIndexSignature(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @filename: /a.tsx\ndeclare namespace JSX {\n  interface IntrinsicElements { [elemName: string]: any; }\n}\n</**/div class=\"democlass\" />;"u8);
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
