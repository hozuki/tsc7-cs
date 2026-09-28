// ported from: internal/fourslash/tests/signatureHelpExplicitTypeArguments_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpExplicitTypeArgumentsCase
{
    public static void TestSignatureHelpExplicitTypeArguments(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function f<T = boolean, U = string>(x: T, y: U): T;\nf<number, string>(/*1*/);\nf(/*2*/);\nf<number>(/*3*/);\nf<number, string, boolean>(/*4*/);\ninterface A { a: number }\ninterface B extends A { b: string }\ndeclare function g<T, U, V extends A = B>(x: T, y: U, z: V): T;\ndeclare function h<T, U, V extends A>(x: T, y: U, z: V): T;\ndeclare function j<T, U, V = B>(x: T, y: U, z: V): T;\ng(/*5*/);\nh(/*6*/);\nj(/*7*/);\ng<number>(/*8*/);\nh<number>(/*9*/);\nj<number>(/*10*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(x: number, y: string): number" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(x: boolean, y: string): boolean" });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(x: number, y: string): number" });
                f.GoToMarker(t, "4");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f(x: number, y: string): number" });
                f.GoToMarker(t, "5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "g(x: unknown, y: unknown, z: B): unknown" });
                f.GoToMarker(t, "6");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "h(x: unknown, y: unknown, z: A): unknown" });
                f.GoToMarker(t, "7");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "j(x: unknown, y: unknown, z: B): unknown" });
                f.GoToMarker(t, "8");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "g(x: number, y: unknown, z: B): number" });
                f.GoToMarker(t, "9");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "h(x: number, y: unknown, z: A): number" });
                f.GoToMarker(t, "10");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "j(x: number, y: unknown, z: B): number" });
            }
            finally
            {
                done();
            }
        });
    }

}
