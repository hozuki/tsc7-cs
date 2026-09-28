// ported from: internal/fourslash/tests/gotoDefinitionInObjectBindingPattern2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GotoDefinitionInObjectBindingPattern2Case
{
    public static void TestGotoDefinitionInObjectBindingPattern2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var p0 = ({a/*1*/a}) => {console.log(aa)};\nfunction f2({ [|a/*a1*/1|], [|b/*b1*/1|] }: { /*a1_dest*/a1: number, /*b1_dest*/b1: number } = { a1: 0, b1: 0 }) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1", "a1", "b1");
            }
            finally
            {
                done();
            }
        });
    }

}
