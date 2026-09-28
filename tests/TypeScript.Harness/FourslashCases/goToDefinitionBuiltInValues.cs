// ported from: internal/fourslash/tests/goToDefinitionBuiltInValues_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionBuiltInValuesCase
{
    public static void TestGoToDefinitionBuiltInValues(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var u = /*undefined*/undefined;\nvar n = /*null*/null;\nvar a = function() { return /*arguments*/arguments; };\nvar t = /*true*/true;\nvar f = /*false*/false;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, f.MarkerNames());
            }
            finally
            {
                done();
            }
        });
    }

}
