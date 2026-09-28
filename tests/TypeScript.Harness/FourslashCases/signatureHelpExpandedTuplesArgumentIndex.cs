// ported from: internal/fourslash/tests/signatureHelpExpandedTuplesArgumentIndex_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpExpandedTuplesArgumentIndexCase
{
    public static void TestSignatureHelpExpandedTuplesArgumentIndex(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(...args: [string, string] | [number, string, string]\n) {\n\n}\n\nfoo(123/*1*/,)\nfoo(\"\"/*2*/, \"\"/*3*/)\nfoo(123/*4*/, \"\"/*5*/, )\nfoo(123/*6*/, \"\"/*7*/, \"\"/*8*/)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(args_0: number, args_1: string, args_2: string): void", ParameterCount = 3, ParameterName = "args_0", ParameterSpan = "args_0: number", OverloadsCount = 2, OverrideSelectedItemIndex = 1, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(args_0: string, args_1: string): void", ParameterCount = 2, ParameterName = "args_0", ParameterSpan = "args_0: string", OverloadsCount = 2, OverrideSelectedItemIndex = 0, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(args_0: string, args_1: string): void", ParameterCount = 2, ParameterName = "args_1", ParameterSpan = "args_1: string", OverloadsCount = 2, OverrideSelectedItemIndex = 0, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(args_0: number, args_1: string, args_2: string): void", ParameterCount = 3, ParameterName = "args_0", ParameterSpan = "args_0: number", OverloadsCount = 2, OverrideSelectedItemIndex = 1, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(args_0: number, args_1: string, args_2: string): void", ParameterCount = 3, ParameterName = "args_1", ParameterSpan = "args_1: string", OverloadsCount = 2, OverrideSelectedItemIndex = 1, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "6");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(args_0: number, args_1: string, args_2: string): void", ParameterCount = 3, ParameterName = "args_0", ParameterSpan = "args_0: number", OverloadsCount = 2, OverrideSelectedItemIndex = 1, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "7");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(args_0: number, args_1: string, args_2: string): void", ParameterCount = 3, ParameterName = "args_1", ParameterSpan = "args_1: string", OverloadsCount = 2, OverrideSelectedItemIndex = 1, IsVariadic = false, IsVariadicSet = true });
                f.GoToMarker(t, "8");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(args_0: number, args_1: string, args_2: string): void", ParameterCount = 3, ParameterName = "args_2", ParameterSpan = "args_2: string", OverloadsCount = 2, OverrideSelectedItemIndex = 1, IsVariadic = false, IsVariadicSet = true });
            }
            finally
            {
                done();
            }
        });
    }

}
