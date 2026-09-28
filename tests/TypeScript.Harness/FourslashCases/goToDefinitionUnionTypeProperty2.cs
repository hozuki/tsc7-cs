// ported from: internal/fourslash/tests/goToDefinitionUnionTypeProperty2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionUnionTypeProperty2Case
{
    public static void TestGoToDefinitionUnionTypeProperty2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface HasAOrB {\n    /*propertyDefinition1*/a: string;\n    b: string;\n}\n\ninterface One {\n    common: { /*propertyDefinition2*/a : number; };\n}\n\ninterface Two {\n    common: HasAOrB;\n}\n\nvar x : One | Two;\n\nx.common.[|/*propertyReference*/a|];"u8);
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
