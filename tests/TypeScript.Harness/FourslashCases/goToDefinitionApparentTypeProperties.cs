// ported from: internal/fourslash/tests/goToDefinitionApparentTypeProperties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionApparentTypePropertiesCase
{
    public static void TestGoToDefinitionApparentTypeProperties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Number {\n    /*definition*/myObjectMethod(): number;\n}\n\nvar o = 0;\no.[|/*reference1*/myObjectMethod|]();\no[[|\"/*reference2*/myObjectMethod\"|]]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "reference1", "reference2");
            }
            finally
            {
                done();
            }
        });
    }

}
