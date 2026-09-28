// ported from: internal/fourslash/tests/signatureHelpAnonymousFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpAnonymousFunctionCase
{
    public static void TestSignatureHelpAnonymousFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var anonymousFunctionTest = function(n: number, s: string): (a: number, b: string) => string {\n    return null;\n}\nanonymousFunctionTest(5, \"\")(/*anonymousFunction1*/1, /*anonymousFunction2*/\"\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "anonymousFunction1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "(a: number, b: string): string", ParameterCount = 2, ParameterName = "a", ParameterSpan = "a: number" });
                f.GoToMarker(t, "anonymousFunction2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "b", ParameterSpan = "b: string" });
            }
            finally
            {
                done();
            }
        });
    }

}
