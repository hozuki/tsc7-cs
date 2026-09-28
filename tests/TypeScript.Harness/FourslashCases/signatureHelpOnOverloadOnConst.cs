// ported from: internal/fourslash/tests/signatureHelpOnOverloadOnConst_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpOnOverloadOnConstCase
{
    public static void TestSignatureHelpOnOverloadOnConst(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function x1(x: 'hi');\nfunction x1(y: 'bye');\nfunction x1(z: string);\nfunction x1(a: any) {\n}\n\nx1(''/*1*/);\nx1('hi'/*2*/);\nx1('bye'/*3*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "z", ParameterSpan = "z: string", OverloadsCount = 3 });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "x", ParameterSpan = "x: 'hi'", OverloadsCount = 3 });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "y", ParameterSpan = "y: 'bye'", OverloadsCount = 3 });
            }
            finally
            {
                done();
            }
        });
    }

}
