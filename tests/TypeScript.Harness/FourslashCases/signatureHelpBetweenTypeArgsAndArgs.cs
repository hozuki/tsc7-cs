// ported from: internal/fourslash/tests/signatureHelpBetweenTypeArgsAndArgs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpBetweenTypeArgsAndArgsCase
{
    public static void TestSignatureHelpTokenCrash2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nfunction foo<T, U>(x: string, y: T, z: U) {\n\n}\n\nfoo<number,number>/*1*/(\"hello\", 123,456)\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySignatureHelpWithCases(t, new F.SignatureHelpCase { MarkerInput = "1", Expected = null!, Context = new LSP.SignatureHelpContext { IsRetrigger = false, TriggerCharacter = "(", TriggerKind = LSP.SignatureHelpTriggerKind.TriggerCharacter } });
            }
            finally
            {
                done();
            }
        });
    }

}
