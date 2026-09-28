// ported from: internal/fourslash/tests/goToDefinitionMultipleDefinitions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionMultipleDefinitionsCase
{
    public static void TestGoToDefinitionMultipleDefinitions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\ninterface /*interfaceDefinition1*/IFoo {\n    instance1: number;\n}\n// @Filename: b.ts\ninterface /*interfaceDefinition2*/IFoo {\n    instance2: number;\n}\n\ninterface /*interfaceDefinition3*/IFoo {\n    instance3: number;\n}\n\nvar ifoo: [|IFo/*interfaceReference*/o|];\n// @Filename: c.ts\nmodule /*moduleDefinition1*/Module {\n    export class c1 { }\n}\n// @Filename: d.ts\nmodule /*moduleDefinition2*/Module {\n    export class c2 { }\n}\n// @Filename: e.ts\n[|Modul/*moduleReference*/e|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "interfaceReference", "moduleReference");
            }
            finally
            {
                done();
            }
        });
    }

}
