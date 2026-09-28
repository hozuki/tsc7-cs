// ported from: internal/fourslash/tests/signatureHelpConstructExpression_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpConstructExpressionCase
{
    public static void TestSignatureHelpConstructExpression(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class sampleCls { constructor(str: string, num: number) { } }\nvar x = new sampleCls(/*1*/\"\", /*2*/5);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "sampleCls(str: string, num: number): sampleCls", ParameterCount = 2, ParameterName = "str", ParameterSpan = "str: string" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "num", ParameterSpan = "num: number" });
            }
            finally
            {
                done();
            }
        });
    }

}
