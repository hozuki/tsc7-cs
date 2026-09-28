// ported from: internal/fourslash/tests/signatureHelpNestedCallTrailingComma_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpNestedCallTrailingCommaCase
{
    public static void TestSignatureHelpNestedCallTrailingComma(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function outer<T>(range: T): T;\ndeclare function inner(a: any): any;\n\nouter(inner/*1*/(undefined,),);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelpPresent(t, new LSP.SignatureHelpContext { IsRetrigger = false, TriggerKind = LSP.SignatureHelpTriggerKind.Invoked });
            }
            finally
            {
                done();
            }
        });
    }

}
