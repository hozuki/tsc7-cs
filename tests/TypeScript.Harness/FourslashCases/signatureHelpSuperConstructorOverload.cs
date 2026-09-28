// ported from: internal/fourslash/tests/signatureHelpSuperConstructorOverload_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpSuperConstructorOverloadCase
{
    public static void TestSignatureHelpSuperConstructorOverload(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class SuperOverloadBase {\n    constructor();\n    constructor(test: string);\n    constructor(test?: string) {\n    }\n}\nclass SuperOverLoad1 extends SuperOverloadBase {\n    constructor() {\n        super(/*superOverload1*/);\n    }\n}\nclass SuperOverLoad2 extends SuperOverloadBase {\n    constructor() {\n        super(\"\"/*superOverload2*/);\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "superOverload1");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "SuperOverloadBase(): SuperOverloadBase", ParameterCount = 0, OverloadsCount = 2 });
                f.GoToMarker(t, "superOverload2");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "SuperOverloadBase(test: string): SuperOverloadBase", ParameterCount = 1, ParameterName = "test", ParameterSpan = "test: string", OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
