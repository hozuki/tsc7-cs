// ported from: internal/fourslash/tests/goToDefinitionShorthandProperty01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionShorthandProperty01Case
{
    public static void TestGoToDefinitionShorthandProperty01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nvar /*valueDeclaration1*/name = \"hello\";\nvar /*valueDeclaration2*/id = 100000;\ndeclare var /*valueDeclaration3*/id;\nvar obj = {[|/*valueDefinition1*/name|], [|/*valueDefinition2*/id|]};\nobj.[|/*valueReference1*/name|];\nobj.[|/*valueReference2*/id|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "valueDefinition1", "valueDefinition2", "valueReference1", "valueReference2");
            }
            finally
            {
                done();
            }
        });
    }

}
