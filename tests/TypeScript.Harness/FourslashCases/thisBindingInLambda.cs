// ported from: internal/fourslash/tests/thisBindingInLambda_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ThisBindingInLambdaCase
{
    public static void TestThisBindingInLambda(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Greeter {\n    constructor() { \n\t\t[].forEach((anything)=>{\n\t\t\tconsole.log(th/**/is);\n\t\t});\n\t}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "this: this", "");
            }
            finally
            {
                done();
            }
        });
    }

}
