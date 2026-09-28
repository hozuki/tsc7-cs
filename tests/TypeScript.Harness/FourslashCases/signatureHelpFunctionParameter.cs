// ported from: internal/fourslash/tests/signatureHelpFunctionParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpFunctionParameterCase
{
    public static void TestSignatureHelpFunctionParameter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function parameterFunction(callback: (a: number, b: string) => void) {\n    callback(/*parameterFunction1*/5, /*parameterFunction2*/\"\");\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "parameterFunction1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callback(a: number, b: string): void", ParameterCount = 2, ParameterName = "a", ParameterSpan = "a: number" });
                f.GoToMarker(t, "parameterFunction2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "callback(a: number, b: string): void", ParameterName = "b", ParameterSpan = "b: string" });
            }
            finally
            {
                done();
            }
        });
    }

}
