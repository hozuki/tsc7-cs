// ported from: internal/fourslash/tests/signatureHelpJsxTextLessThanTrigger_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpJsxTextLessThanTriggerCase
{
    public static void TestSignatureHelpJsxTextLessThanTrigger(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: test.tsx\n//@jsx: react\ndeclare var React: any;\ndeclare function Text(props: { children?: any }): any;\n\nconst text = () => {\n\treturn <Text>/*m*/</Text>;\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "m");
                f.Insert(t, "<");
                f.VerifyNoSignatureHelpWithContext(t, new LSP.SignatureHelpContext { TriggerKind = LSP.SignatureHelpTriggerKind.TriggerCharacter, TriggerCharacter = "<", IsRetrigger = false });
            }
            finally
            {
                done();
            }
        });
    }

}
