// ported from: internal/fourslash/tests/gotoDefinitionInObjectBindingPattern1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GotoDefinitionInObjectBindingPattern1Case
{
    public static void TestGotoDefinitionInObjectBindingPattern1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function bar<T>(onfulfilled: (value: T) => void) {\n  return undefined;\n}\ninterface Test {\n  /*destination*/prop2: number\n}\nbar<Test>(({[|pr/*goto*/op2|]})=>{});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "goto");
            }
            finally
            {
                done();
            }
        });
    }

}
