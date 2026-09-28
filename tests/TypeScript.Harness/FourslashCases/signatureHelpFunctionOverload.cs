// ported from: internal/fourslash/tests/signatureHelpFunctionOverload_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpFunctionOverloadCase
{
    public static void TestSignatureHelpFunctionOverload(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function functionOverload();\nfunction functionOverload(test: string);\nfunction functionOverload(test?: string) { }\nfunctionOverload(/*functionOverload1*/);\nfunctionOverload(\"\"/*functionOverload2*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "functionOverload1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "functionOverload(): any", ParameterCount = 0, OverloadsCount = 2 });
                f.GoToMarker(t, "functionOverload2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "functionOverload(test: string): any", ParameterName = "test", ParameterSpan = "test: string", OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
