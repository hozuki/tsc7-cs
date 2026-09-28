// ported from: internal/fourslash/tests/quickInfoElementAccessDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoElementAccessDeclarationCase
{
    public static void TestQuickInfoElementAccessDeclaration(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @allowJs: true\n// @Filename: a.js\nconst mod = {};\nmod[\"@@thing1\"] = {};\nmod[\"/**/@@thing1\"][\"@@thing2\"] = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoIs(t, "module mod[\"@@thing1\"]\n(property) mod[\"@@thing1\"]: typeof mod.@@thing1", "");
            }
            finally
            {
                done();
            }
        });
    }

}
