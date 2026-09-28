// ported from: internal/fourslash/tests/signatureHelpTypeArguments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpTypeArgumentsCase
{
    public static void TestSignatureHelpTypeArguments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function f(a: number, b: string, c: boolean): void; // ignored, not generic\ndeclare function f<T extends number>(): void;\ndeclare function f<T, U>(): void;\ndeclare function f<T, U, V extends string>(): void;\nf</*f0*/;\nf<number, /*f1*/;\nf<number, string, /*f2*/;\n\ndeclare const C: {\n    new<T extends number>(): void;\n    new<T, U>(): void;\n    new<T, U, V extends string>(): void;\n};\nnew C</*C0*/;\nnew C<number, /*C1*/;\nnew C<number, string, /*C2*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "f0");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f<T extends number>(): void", ParameterName = "T", ParameterSpan = "T extends number", OverloadsCount = 3 });
                f.GoToMarker(t, "f1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f<T, U>(): void", ParameterName = "U", ParameterSpan = "U", OverloadsCount = 2 });
                f.GoToMarker(t, "f2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f<T, U, V extends string>(): void", ParameterName = "V", ParameterSpan = "V extends string" });
                f.GoToMarker(t, "C0");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "C<T extends number>(): void", ParameterName = "T", ParameterSpan = "T extends number", OverloadsCount = 3 });
                f.GoToMarker(t, "C1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "C<T, U>(): void", ParameterName = "U", ParameterSpan = "U", OverloadsCount = 2 });
                f.GoToMarker(t, "C2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "C<T, U, V extends string>(): void", ParameterName = "V", ParameterSpan = "V extends string" });
            }
            finally
            {
                done();
            }
        });
    }

}
