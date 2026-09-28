// ported from: internal/fourslash/tests/signatureHelpConstructorOverload_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpConstructorOverloadCase
{
    public static void TestSignatureHelpConstructorOverload(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class clsOverload { constructor(); constructor(test: string); constructor(test?: string) { } }\nvar x = new clsOverload(/*1*/);\nvar y = new clsOverload(/*2*/'');"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "clsOverload(): clsOverload", ParameterCount = 0, OverloadsCount = 2 });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "clsOverload(test: string): clsOverload", ParameterCount = 1, ParameterName = "test", ParameterSpan = "test: string", OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
