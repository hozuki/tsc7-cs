// ported from: internal/fourslash/tests/signatureHelpSimpleSuperCall_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpSimpleSuperCallCase
{
    public static void TestSignatureHelpSimpleSuperCall(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class SuperCallBase {\n    constructor(b: boolean) {\n    }\n}\nclass SuperCall extends SuperCallBase {\n    constructor() {\n        super(/*superCall*/);\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "superCall");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "SuperCallBase(b: boolean): SuperCallBase", ParameterName = "b", ParameterSpan = "b: boolean" });
            }
            finally
            {
                done();
            }
        });
    }

}
