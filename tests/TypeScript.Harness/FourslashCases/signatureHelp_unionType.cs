// ported from: internal/fourslash/tests/signatureHelp_unionType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelp_unionTypeCase
{
    public static void TestSignatureHelp_unionType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare const a: (fn?: ((x: string) => string) | ((y: number) => number)) => void;\ndeclare const b: (x: string | number) => void;\n\ninterface Callback {\n    (x: string): string;\n    (x: number): number;\n    (x: string | number): string | number;\n}\ndeclare function c(callback: Callback): void;\na((/*1*/) => {\n    return undefined;\n});\n\nb(/*2*/);\n\nc((/*3*/) => {});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
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
