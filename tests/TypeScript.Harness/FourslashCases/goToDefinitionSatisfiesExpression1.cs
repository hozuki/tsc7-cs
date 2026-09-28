// ported from: internal/fourslash/tests/goToDefinitionSatisfiesExpression1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionSatisfiesExpression1Case
{
    public static void TestGoToDefinitionSatisfiesExpression1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const STRINGS = {\n    [|/*definition*/title|]: 'A Title',\n} satisfies Record<string,string>;\n\n//somewhere in app\nSTRINGS.[|/*usage*/title|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "definition", "usage");
            }
            finally
            {
                done();
            }
        });
    }

}
