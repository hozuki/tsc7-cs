// ported from: internal/fourslash/tests/signatureHelpEmptyList_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpEmptyListCase
{
    public static void TestSignatureHelpEmptyList(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function Foo(arg1: string, arg2: string) {\n}\n\nFoo(/*1*/);\nfunction Bar<T>(arg1: string, arg2: string) { }\nBar</*2*/>();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "Foo(arg1: string, arg2: string): void", ParameterCount = 2, ParameterName = "arg1", ParameterSpan = "arg1: string" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "Bar<T>(arg1: string, arg2: string): void" });
            }
            finally
            {
                done();
            }
        });
    }

}
