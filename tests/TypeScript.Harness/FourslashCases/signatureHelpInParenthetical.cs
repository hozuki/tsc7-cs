// ported from: internal/fourslash/tests/signatureHelpInParenthetical_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpInParentheticalCase
{
    public static void TestSignatureHelpInParenthetical(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class base { constructor (public n: number, public y: string) { } }\n(new base(/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "n" });
                f.Insert(t, "0, ");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "y" });
            }
            finally
            {
                done();
            }
        });
    }

}
