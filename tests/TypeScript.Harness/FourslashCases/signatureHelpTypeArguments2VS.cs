// ported from: internal/fourslash/tests/signatureHelpTypeArguments2VS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpTypeArguments2VSCase
{
    public static void TestSignatureHelpTypeArguments2VS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** some documentation\n * @template T some documentation 2\n * @template W\n * @template U,V others\n * @param a ok\n * @param b not ok\n */\nfunction f<T, U, V, W>(a: number, b: string, c: boolean): void { }\nf</*f0*/;\nf<number, /*f1*/;\nf<number, string, /*f2*/;\nf<number, string, boolean, /*f3*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, new LSP.ClientCapabilities { VSSupportsVisualStudioExtensions = true }, content);
            try
            {
                f.VerifyBaselineSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
