// ported from: internal/fourslash/tests/goToDefinitionObjectLiteralProperties1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionObjectLiteralProperties1Case
{
    public static void TestGoToDefinitionObjectLiteralProperties1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface PropsBag {\n   /*first*/propx: number\n}\nfunction foo(arg: PropsBag) {}\nfoo({\n   [|pr/*p1*/opx|]: 10\n})\nfunction bar(firstarg: boolean, secondarg: PropsBag) {}\nbar(true, {\n   [|pr/*p2*/opx|]: 10\n})"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "p1", "p2");
            }
            finally
            {
                done();
            }
        });
    }

}
