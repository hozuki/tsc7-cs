// ported from: internal/fourslash/tests/goToTypeDefinitionAliases_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeDefinitionAliasesCase
{
    public static void TestGoToTypeDefinitionAliases(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: goToTypeDefinitioAliases_module1.ts\ninterface /*definition*/I {\n    p;\n}\nexport {I as I2};\n// @Filename: goToTypeDefinitioAliases_module2.ts\nimport {I2 as I3} from \"./goToTypeDefinitioAliases_module1\";\nvar v1: I3;\nexport {v1 as v2};\n// @Filename: goToTypeDefinitioAliases_module3.ts\nimport {/*reference1*/v2 as v3} from \"./goToTypeDefinitioAliases_module2\";\n/*reference2*/v3;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, "reference1", "reference2");
            }
            finally
            {
                done();
            }
        });
    }

}
