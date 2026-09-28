// ported from: internal/fourslash/tests/goToDefinitionOverriddenMember9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionOverriddenMember9Case
{
    public static void TestGoToDefinitionOverriddenMember9(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noImplicitOverride: true\ninterface I {\n    m(): void;\n}\nclass A {\n    /*2*/m() {};\n}\nclass B extends A implements I {\n   [|/*1*/override|] m() {}\n}"u8);
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
