// ported from: internal/fourslash/tests/signatureHelpOnNestedOverloads_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpOnNestedOverloadsCase
{
    public static void TestSignatureHelpOnNestedOverloads(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function fn(x: string);\ndeclare function fn(x: string, y: number);\ndeclare function fn2(x: string);\ndeclare function fn2(x: string, y: number);\nfn('', fn2(/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "fn2(x: string): any", ParameterName = "x", ParameterSpan = "x: string", OverloadsCount = 2 });
                f.Insert(t, "'',");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "fn2(x: string, y: number): any", ParameterName = "y", ParameterSpan = "y: number", OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
