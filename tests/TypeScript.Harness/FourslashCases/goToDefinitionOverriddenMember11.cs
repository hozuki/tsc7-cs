// ported from: internal/fourslash/tests/goToDefinitionOverriddenMember11_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionOverriddenMember11Case
{
    public static void TestGoToDefinitionOverriddenMember11(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @noEmit: true\n// @noImplicitOverride: true\n// @filename: a.js\nclass Foo {\n    /*Foo_m*/m() {}\n}\nclass Bar extends Foo {\n    /** @[|over{|\"name\": \"1\"|}ride|][| se{|\"name\": \"2\"|}e {@li{|\"name\": \"3\"|}nk https://test.c{|\"name\": \"4\"|}om} {|\"name\": \"5\"|}description |]*/\n    m() {}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "1", "2", "3", "4", "5");
            }
            finally
            {
                done();
            }
        });
    }

}
