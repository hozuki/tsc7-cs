// ported from: internal/fourslash/tests/goToDefinitionSameFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionSameFileCase
{
    public static void TestGoToDefinitionSameFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var /*localVariableDefinition*/localVariable;\nfunction /*localFunctionDefinition*/localFunction() { }\nclass /*localClassDefinition*/localClass { }\ninterface /*localInterfaceDefinition*/localInterface{ }\nmodule /*localModuleDefinition*/localModule{ export var foo = 1;}\n\n\n/*localVariableReference*/localVariable = 1;\n/*localFunctionReference*/localFunction();\nvar foo = new /*localClassReference*/localClass();\nclass fooCls implements /*localInterfaceReference*/localInterface { }\nvar fooVar = /*localModuleReference*/localModule.foo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, false, "localVariableReference", "localFunctionReference", "localClassReference", "localInterfaceReference", "localModuleReference");
            }
            finally
            {
                done();
            }
        });
    }

}
