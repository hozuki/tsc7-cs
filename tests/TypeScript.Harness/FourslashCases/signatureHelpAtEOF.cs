// ported from: internal/fourslash/tests/signatureHelpAtEOF_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpAtEOFCase
{
    public static void TestSignatureHelpAtEOF(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function Foo(arg1: string, arg2: string) {\n}\n\nFoo(/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "Foo(arg1: string, arg2: string): void", ParameterCount = 2, ParameterName = "arg1", ParameterSpan = "arg1: string" });
            }
            finally
            {
                done();
            }
        });
    }

}
