// ported from: internal/fourslash/tests/signatureHelpWithTriggers02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpWithTriggers02Case
{
    public static void TestSignatureHelpWithTriggers02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function foo<T>(x: T, y: T): T;\ndeclare function bar<U>(x: U, y: U): U;\n\nfoo(bar/*1*/)"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "(");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "bar(x: unknown, y: unknown): unknown" });
                f.Backspace(t, 1);
                f.Insert(t, "<");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "bar<U>(x: U, y: U): U" });
                f.Backspace(t, 1);
                f.Insert(t, ",");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(x: <U>(x: U, y: U) => U, y: <U>(x: U, y: U) => U): <U>(x: U, y: U) => U" });
                f.Backspace(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
