// ported from: internal/fourslash/tests/signatureHelpCallExpressionTuples_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpCallExpressionTuplesCase
{
    public static void TestSignatureHelpCallExpressionTuples(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function fnTest(str: string, num: number) { }\ndeclare function wrap<A extends any[], R>(fn: (...a: A) => R) : (...a: A) => R;\nvar fnWrapped = wrap(fnTest);\nfnWrapped/*3*/(/*1*/'', /*2*/5);\nfunction fnTestVariadic (str: string, ...num: number[]) { }\nvar fnVariadicWrapped = wrap(fnTestVariadic);\nfnVariadicWrapped/*4*/(/*5*/'', /*6*/5);\nfunction fnNoParams () { }\nvar fnNoParamsWrapped = wrap(fnNoParams);\nfnNoParamsWrapped/*7*/(/*8*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "3", "var fnWrapped: (str: string, num: number) => void", "");
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "fnWrapped(str: string, num: number): void", ParameterCount = 2, ParameterName = "str", ParameterSpan = "str: string" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "num", ParameterSpan = "num: number" });
                f.VerifyQuickInfoAt(t, "4", "var fnVariadicWrapped: (str: string, ...num: number[]) => void", "");
                f.GoToMarker(t, "5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "fnVariadicWrapped(str: string, ...num: number[]): void", ParameterCount = 2, ParameterName = "str", ParameterSpan = "str: string", IsVariadic = true, IsVariadicSet = true });
                f.GoToMarker(t, "6");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { ParameterName = "num", ParameterSpan = "...num: number[]", IsVariadic = true, IsVariadicSet = true });
                f.VerifyQuickInfoAt(t, "7", "var fnNoParamsWrapped: () => void", "");
                f.GoToMarker(t, "8");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "fnNoParamsWrapped(): void", ParameterCount = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
