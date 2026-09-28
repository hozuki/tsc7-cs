// ported from: internal/fourslash/tests/signatureHelp_contextual_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelp_contextualCase
{
    public static void TestSignatureHelp_contextual(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface I {\n    m(n: number, s: string): void;\n    m2: () => void;\n}\ndeclare function takesObj(i: I): void;\ntakesObj({ m: (/*takesObj0*/) });\ntakesObj({ m(/*takesObj1*/) });\ntakesObj({ m: function(/*takesObj2*/) });\ntakesObj({ m2: (/*takesObj3*/) });\n\ndeclare function takesCb(cb: (n: number, s: string, b: boolean) => void): void;\ntakesCb((/*contextualParameter1*/));\ntakesCb((/*contextualParameter1b*/) => {});\ntakesCb((n, /*contextualParameter2*/));\ntakesCb((n, s, /*contextualParameter3*/));\ntakesCb((n,/*contextualParameter3_2*/ s, b));\ntakesCb((n, s, b, /*contextualParameter4*/));\n\ntype Cb = () => void;\nconst cb: Cb = (/*contextualTypeAlias*/)\n\nconst cb2: () => void = (/*contextualFunctionType*/)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "takesObj0");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "m(n: number, s: string): void", ParameterCount = 2, ParameterName = "n", ParameterSpan = "n: number" });
                f.GoToMarker(t, "takesObj1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "m(n: number, s: string): void", ParameterCount = 2, ParameterName = "n", ParameterSpan = "n: number" });
                f.GoToMarker(t, "takesObj2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "m(n: number, s: string): void", ParameterCount = 2, ParameterName = "n", ParameterSpan = "n: number" });
                f.GoToMarker(t, "takesObj3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "m2(): void", ParameterCount = 0 });
                f.GoToMarker(t, "contextualParameter1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "cb(n: number, s: string, b: boolean): void", ParameterCount = 3, ParameterName = "n", ParameterSpan = "n: number" });
                f.GoToMarker(t, "contextualParameter1b");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "cb(n: number, s: string, b: boolean): void", ParameterCount = 3, ParameterName = "n", ParameterSpan = "n: number" });
                f.GoToMarker(t, "contextualParameter2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "cb(n: number, s: string, b: boolean): void", ParameterCount = 3, ParameterName = "s", ParameterSpan = "s: string" });
                f.GoToMarker(t, "contextualParameter3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "cb(n: number, s: string, b: boolean): void", ParameterCount = 3, ParameterName = "b", ParameterSpan = "b: boolean" });
                f.GoToMarker(t, "contextualParameter3_2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "cb(n: number, s: string, b: boolean): void", ParameterCount = 3, ParameterName = "s", ParameterSpan = "s: string" });
                f.GoToMarker(t, "contextualParameter4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "cb(n: number, s: string, b: boolean): void", ParameterCount = 3 });
                f.GoToMarker(t, "contextualTypeAlias");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "Cb(): void", ParameterCount = 0 });
                f.GoToMarker(t, "contextualFunctionType");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "cb2(): void", ParameterCount = 0 });
            }
            finally
            {
                done();
            }
        });
    }

}
