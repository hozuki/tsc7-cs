// ported from: internal/fourslash/tests/goToDefinitionAmbiants_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionAmbiantsCase
{
    public static void TestGoToDefinitionAmbiants(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare var /*ambientVariableDefinition*/ambientVar;\ndeclare function /*ambientFunctionDefinition*/ambientFunction();\ndeclare class ambientClass {\n    /*constructorDefinition*/constructor();\n    static /*staticMethodDefinition*/method();\n    public /*instanceMethodDefinition*/method();\n}\n\n/*ambientVariableReference*/ambientVar = 1;\n/*ambientFunctionReference*/ambientFunction();\nvar ambientClassVariable = new /*constructorReference*/ambientClass();\nambientClass./*staticMethodReference*/method();\nambientClassVariable./*instanceMethodReference*/method();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, false, "ambientVariableReference", "ambientFunctionReference", "constructorReference", "staticMethodReference", "instanceMethodReference");
            }
            finally
            {
                done();
            }
        });
    }

}
