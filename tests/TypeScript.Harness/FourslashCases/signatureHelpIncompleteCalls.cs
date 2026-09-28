// ported from: internal/fourslash/tests/signatureHelpIncompleteCalls_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpIncompleteCallsCase
{
    public static void TestSignatureHelpIncompleteCalls(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace IncompleteCalls {\n    class Foo {\n        public f1() { }\n        public f2(n: number): number { return 0; }\n        public f3(n: number, s: string) : string { return \"\"; }\n    }\n    var x = new Foo();\n    x.f1();\n    x.f2(5);\n    x.f3(5, \"\");\n    x.f1(/*incompleteCalls1*/\n    x.f2(5,/*incompleteCalls2*/\n    x.f3(5,/*incompleteCalls3*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "incompleteCalls1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f1(): void", ParameterCount = 0 });
                f.GoToMarker(t, "incompleteCalls2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f2(n: number): number", ParameterCount = 1 });
                f.GoToMarker(t, "incompleteCalls3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "f3(n: number, s: string): string", ParameterCount = 2, ParameterName = "s", ParameterSpan = "s: string" });
            }
            finally
            {
                done();
            }
        });
    }

}
