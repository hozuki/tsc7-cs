// ported from: internal/fourslash/tests/signatureHelpForSuperCalls1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpForSuperCalls1Case
{
    public static void TestSignatureHelpForSuperCalls1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A { }\nclass B extends A { }\nclass C extends B {\n    constructor() {\n        super(/*1*/ // sig help here?\n    }\n}\nclass A2 { }\nclass B2 extends A2 {\n    constructor(x:number) {}\n }\nclass C2 extends B2 {\n    constructor() {\n        super(/*2*/ // sig help here?\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "B(): B" });
                f.GoToMarker(t, "2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "B2(x: number): B2" });
            }
            finally
            {
                done();
            }
        });
    }

}
