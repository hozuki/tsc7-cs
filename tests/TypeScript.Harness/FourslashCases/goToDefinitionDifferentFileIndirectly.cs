// ported from: internal/fourslash/tests/goToDefinitionDifferentFileIndirectly_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionDifferentFileIndirectlyCase
{
    public static void TestGoToDefinitionDifferentFileIndirectly(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: Remote2.ts\nvar /*remoteVariableDefinition*/rem2Var;\nfunction /*remoteFunctionDefinition*/rem2Fn() { }\nclass /*remoteClassDefinition*/rem2Cls { }\ninterface /*remoteInterfaceDefinition*/rem2Int{}\nmodule /*remoteModuleDefinition*/rem2Mod { export var foo; }\n// @Filename: Remote1.ts\nvar remVar;\nfunction remFn() { }\nclass remCls { }\ninterface remInt{}\nnamespace remMod { export var foo; }\n// @Filename: Definition.ts\n/*remoteVariableReference*/rem2Var = 1;\n/*remoteFunctionReference*/rem2Fn();\nvar rem2foo = new /*remoteClassReference*/rem2Cls();\nclass rem2fooCls implements /*remoteInterfaceReference*/rem2Int { }\nvar rem2fooVar = /*remoteModuleReference*/rem2Mod.foo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, false, "remoteVariableReference", "remoteFunctionReference", "remoteClassReference", "remoteInterfaceReference", "remoteModuleReference");
            }
            finally
            {
                done();
            }
        });
    }

}
