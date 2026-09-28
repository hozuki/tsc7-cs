// ported from: internal/fourslash/tests/goToDefinitionOverriddenMember6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionOverriddenMember6Case
{
    public static void TestGoToDefinitionOverriddenMember6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noImplicitOverride: true\nclass Foo {\n    m() {}\n}\nclass Bar extends Foo {\n    [|/*1*/override|] m1() {}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
