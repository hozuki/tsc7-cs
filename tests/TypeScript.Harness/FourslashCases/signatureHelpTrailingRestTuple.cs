// ported from: internal/fourslash/tests/signatureHelpTrailingRestTuple_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpTrailingRestTupleCase
{
    public static void TestSignatureHelpTrailingRestTuple(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export function leading(allCaps: boolean, ...names: string[]): void {\n}\n\nleading(/*1*/);\nleading(false, /*2*/);\nleading(false, \"ok\", /*3*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "leading(allCaps: boolean, ...names: string[]): void", ParameterCount = 2, ParameterName = "allCaps", ParameterSpan = "allCaps: boolean", OverloadsCount = 1, IsVariadic = true, IsVariadicSet = true });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "leading(allCaps: boolean, ...names: string[]): void", ParameterCount = 2, ParameterName = "names", ParameterSpan = "...names: string[]", OverloadsCount = 1, IsVariadic = true, IsVariadicSet = true });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "leading(allCaps: boolean, ...names: string[]): void", ParameterCount = 2, ParameterName = "names", ParameterSpan = "...names: string[]", OverloadsCount = 1, IsVariadic = true, IsVariadicSet = true });
            }
            finally
            {
                done();
            }
        });
    }

}
