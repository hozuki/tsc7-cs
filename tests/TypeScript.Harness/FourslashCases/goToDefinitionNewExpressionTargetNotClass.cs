// ported from: internal/fourslash/tests/goToDefinitionNewExpressionTargetNotClass_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionNewExpressionTargetNotClassCase
{
    public static void TestGoToDefinitionNewExpressionTargetNotClass(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C2 {\n}\nlet /*I*/I: {\n    /*constructSignature*/new(): C2;\n};\nnew [|/*invokeExpression1*/I|]();\nlet /*symbolDeclaration*/I2: {\n};\nnew [|/*invokeExpression2*/I2|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "invokeExpression1", "invokeExpression2");
            }
            finally
            {
                done();
            }
        });
    }

}
