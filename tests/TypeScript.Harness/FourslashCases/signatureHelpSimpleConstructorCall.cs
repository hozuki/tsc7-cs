// ported from: internal/fourslash/tests/signatureHelpSimpleConstructorCall_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpSimpleConstructorCallCase
{
    public static void TestSignatureHelpSimpleConstructorCall(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class ConstructorCall {\n    constructor(str: string, num: number) {\n    }\n}\nvar x = new ConstructorCall(/*constructorCall1*/1,/*constructorCall2*/2);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "constructorCall1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "ConstructorCall(str: string, num: number): ConstructorCall", ParameterName = "str", ParameterSpan = "str: string" });
                f.GoToMarker(t, "constructorCall2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "ConstructorCall(str: string, num: number): ConstructorCall", ParameterName = "num", ParameterSpan = "num: number" });
            }
            finally
            {
                done();
            }
        });
    }

}
