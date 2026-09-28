// ported from: internal/fourslash/tests/goToDefinitionAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionAliasCase
{
    public static void TestGoToDefinitionAlias(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: b.ts\nimport /*alias1Definition*/alias1 = require(\"fileb\");\nnamespace Module {\n    export import /*alias2Definition*/alias2 = alias1;\n}\n\n// Type position\nvar t1: [|/*alias1Type*/alias1|].IFoo;\nvar t2: Module.[|/*alias2Type*/alias2|].IFoo;\n\n// Value posistion\nvar v1 = new [|/*alias1Value*/alias1|].Foo();\nvar v2 = new Module.[|/*alias2Value*/alias2|].Foo();\n// @Filename: a.ts\nexport class Foo {\n    private f;\n}\nexport interface IFoo {\n    x;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "alias1Type", "alias1Value", "alias2Type", "alias2Value");
            }
            finally
            {
                done();
            }
        });
    }

}
