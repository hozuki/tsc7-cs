// ported from: internal/fourslash/tests/signatureHelpNestedCalls_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpNestedCallsCase
{
    public static void TestSignatureHelpNestedCalls(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(s: string) { return s; }\nfunction bar(s: string) { return s; }\nlet s = foo(/*a*/ /*b*/bar/*c*/(/*d*/\"hello\"/*e*/)/*f*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "a");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(s: string): string" });
                f.GoToMarker(t, "b");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(s: string): string" });
                f.GoToMarker(t, "c");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(s: string): string" });
                f.GoToMarker(t, "d");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "bar(s: string): string" });
                f.GoToMarker(t, "e");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "bar(s: string): string" });
                f.GoToMarker(t, "f");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "foo(s: string): string" });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestSignatureHelpEmptyInnerCall(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(s: string) { return s; }\nfunction bar(s: string) { return s; }\nlet s = foo(bar(/*a*/));"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "a");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "bar(s: string): string" });
            }
            finally
            {
                done();
            }
        });
    }

}
