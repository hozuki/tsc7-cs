// ported from: internal/fourslash/tests/signatureHelpLeadingRestTuple_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpLeadingRestTupleCase
{
    public static void TestSignatureHelpLeadingRestTuple(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export function leading(...args: [...names: string[], allCaps: boolean]): void {\n}\n\nleading(/*1*/);\nleading(\"ok\", /*2*/);\nleading(\"ok\", \"ok\", /*3*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "leading(...names: string[], allCaps: boolean): void", ParameterCount = 2, OverloadsCount = 1, IsVariadic = true, IsVariadicSet = true });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "leading(...names: string[], allCaps: boolean): void", ParameterCount = 2, OverloadsCount = 1, IsVariadic = true, IsVariadicSet = true });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "leading(...names: string[], allCaps: boolean): void", ParameterCount = 2, OverloadsCount = 1, IsVariadic = true, IsVariadicSet = true });
            }
            finally
            {
                done();
            }
        });
    }

}
