// ported from: internal/fourslash/tests/goToDefinitionObjectBindingPattern_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionObjectBindingPatternCase
{
    public static void TestGoToDefinitionObjectBindingPattern(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface SomeType {\n    targetProperty: number;\n}\n\nfunction foo(callback: (p: SomeType) => void) {}\n\nfoo(({ /*1*/targetProperty }) => {\n    /*4*/targetProperty\n});\n\nlet { /*2*/targetProperty }: SomeType = { /*3*/targetProperty: 42 };\n\nlet { /*5*/targetProperty: /*6*/alias_1 }: SomeType = { targetProperty: 42 };\n\nlet { x: { /*7*/targetProperty: /*8*/{} } }: { x: SomeType } = { x: { targetProperty: 42 } };"u8);
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

    public static void TestGoToDefinitionObjectBindingPatternRest(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ninterface SomeType {\n    targetProperty: number;\n}\n\nlet { .../*1*/rest }: SomeType = { targetProperty: 42 };"u8);
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
