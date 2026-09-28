// ported from: internal/fourslash/tests/signatureHelpOnOverloadsDifferentArity_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpOnOverloadsDifferentArityCase
{
    public static void TestSignatureHelpOnOverloadsDifferentArity(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function f(s: string);\ndeclare function f(n: number);\ndeclare function f(s: string, b: boolean);\ndeclare function f(n: number, b: boolean);\n\nf(1/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(n: number): any", ParameterName = "n", ParameterSpan = "n: number", OverloadsCount = 4 });
                f.Insert(t, ", ");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(n: number, b: boolean): any", ParameterName = "b", ParameterSpan = "b: boolean", OverloadsCount = 4 });
            }
            finally
            {
                done();
            }
        });
    }

}
