// ported from: internal/fourslash/tests/quickInfoJsdocEnum_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsdocEnumCase
{
    public static void TestQuickInfoJsdocEnum(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @noLib: true\n// @Filename: /a.js\n/**\n * Doc\n * @enum {number}\n */\nconst E = {\n    A: 0,\n}\n\n/** @type {/*type*/E} */\nconst x = /*value*/E.A;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyQuickInfoAt(t, "type", "type E = number", "Doc");
                f.VerifyQuickInfoAt(t, "value", "const E: {\n    A: number;\n}", "Doc");
            }
            finally
            {
                done();
            }
        });
    }

}
