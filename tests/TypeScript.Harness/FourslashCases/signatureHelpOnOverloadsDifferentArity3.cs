// ported from: internal/fourslash/tests/signatureHelpOnOverloadsDifferentArity3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpOnOverloadsDifferentArity3Case
{
    public static void TestSignatureHelpOnOverloadsDifferentArity3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function f();\ndeclare function f(s: string);\ndeclare function f(s: string, b: boolean);\ndeclare function f(n: number, b: boolean);\n\nf(/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(): any", ParameterCount = 0, OverloadsCount = 4 });
                f.Insert(t, "x, ");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(s: string, b: boolean): any", ParameterCount = 2, ParameterName = "b", ParameterSpan = "b: boolean", OverloadsCount = 4 });
                f.Insert(t, "x, ");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(s: string, b: boolean): any", ParameterCount = 2, OverloadsCount = 4 });
            }
            finally
            {
                done();
            }
        });
    }

}
