// ported from: internal/fourslash/tests/signatureHelpInference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpInferenceCase
{
    public static void TestSignatureHelpInference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function f<T extends string>(a: T, b: T, c: T): void;\nf(\"x\", /**/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(a: \"x\", b: \"x\", c: \"x\"): void", ParameterCount = 3, ParameterName = "b", ParameterSpan = "b: \"x\"" });
            }
            finally
            {
                done();
            }
        });
    }

}
