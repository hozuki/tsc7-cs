// ported from: internal/fourslash/tests/signatureHelpConstructorInheritance_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpConstructorInheritanceCase
{
    public static void TestSignatureHelpConstructorInheritance(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class base {\n    constructor(s: string);\n    constructor(n: number);\n    constructor(a: any) { }\n}\nclass B1 extends base { }\nclass B2 extends B1 { }\nclass B3 extends B2 {\n    constructor() {\n        super(/*indirectSuperCall*/3);\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "indirectSuperCall");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "B2(n: number): B2", ParameterCount = 1, ParameterName = "n", ParameterSpan = "n: number", OverloadsCount = 2 });
            }
            finally
            {
                done();
            }
        });
    }

}
