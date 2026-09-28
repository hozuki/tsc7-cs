// ported from: internal/fourslash/tests/goToImplementationInterface_10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterface_10Case
{
    public static void TestGoToImplementationInterface_10(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\ninterface /*def*/A {\n\tfoo: boolean;\n}\ninterface [|B|] extends A {\n\tbar: boolean;\n}\nexport class [|C|] implements B {\n\tfoo = true;\n\tbar = true;\n}\nexport class [|D|] extends C { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "def");
            }
            finally
            {
                done();
            }
        });
    }

}
