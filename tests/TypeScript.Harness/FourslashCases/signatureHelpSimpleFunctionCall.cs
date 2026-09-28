// ported from: internal/fourslash/tests/signatureHelpSimpleFunctionCall_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpSimpleFunctionCallCase
{
    public static void TestSignatureHelpSimpleFunctionCall(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// Simple function test\nfunction functionCall(str: string, num: number) {\n}\nfunctionCall(/*functionCall1*/);\nfunctionCall(\"\", /*functionCall2*/1);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "functionCall1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "functionCall(str: string, num: number): void", ParameterName = "str", ParameterSpan = "str: string" });
                f.GoToMarker(t, "functionCall2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "functionCall(str: string, num: number): void", ParameterName = "num", ParameterSpan = "num: number" });
            }
            finally
            {
                done();
            }
        });
    }

}
