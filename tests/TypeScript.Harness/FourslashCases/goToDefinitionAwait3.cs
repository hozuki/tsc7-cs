// ported from: internal/fourslash/tests/goToDefinitionAwait3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionAwait3Case
{
    public static void TestGoToDefinitionAwait3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    notAsync() {\n      [|/*start1*/await|] Promise.resolve(0);\n    }\n\n    async /*end2*/foo() {\n      [|/*start2*/await|] Promise.resolve(0);\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "start1", "start2");
            }
            finally
            {
                done();
            }
        });
    }

}
