// ported from: internal/fourslash/tests/gotoDefinitionPropertyAccessExpressionHeritageClause_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GotoDefinitionPropertyAccessExpressionHeritageClauseCase
{
    public static void TestGotoDefinitionPropertyAccessExpressionHeritageClause(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class B {}\nfunction foo() {\n    return {/*refB*/B: B};\n}\nclass C extends (foo()).[|/*B*/B|] {}\nclass C1 extends foo().[|/*B1*/B|] {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "B", "B1");
            }
            finally
            {
                done();
            }
        });
    }

}
