// ported from: internal/fourslash/tests/goToDefinitionUnionTypeProperty1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionUnionTypeProperty1Case
{
    public static void TestGoToDefinitionUnionTypeProperty1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface One {\n    /*propertyDefinition1*/commonProperty: number;\n    commonFunction(): number;\n}\n\ninterface Two {\n    /*propertyDefinition2*/commonProperty: string\n    commonFunction(): number;\n}\n\nvar x : One | Two;\n\nx.[|/*propertyReference*/commonProperty|];\nx./*3*/commonFunction;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "propertyReference");
            }
            finally
            {
                done();
            }
        });
    }

}
