// ported from: internal/fourslash/tests/goToDefinitionFunctionOverloads_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionFunctionOverloadsCase
{
    public static void TestGoToDefinitionFunctionOverloads(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function [|/*functionOverload1*/functionOverload|](value: number);\nfunction /*functionOverload2*/functionOverload(value: string);\nfunction /*functionOverloadDefinition*/functionOverload() {}\n\n[|/*functionOverloadReference1*/functionOverload|](123);\n[|/*functionOverloadReference2*/functionOverload|](\"123\");\n[|/*brokenOverload*/functionOverload|]({});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "functionOverloadReference1", "functionOverloadReference2", "brokenOverload", "functionOverload1");
            }
            finally
            {
                done();
            }
        });
    }

}
