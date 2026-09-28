// ported from: internal/fourslash/tests/quickInfoJSExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSExportCase
{
    public static void TestQuickInfoJSExport(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.js\n// @allowJs: true\n/**\n * @enum {string}\n */\nconst testString = {\n    one: \"1\",\n    two: \"2\"\n};\n\nexport { test/**/String };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(alias) type testString = string\n(alias) const testString: {\n    one: string;\n    two: string;\n}\nexport testString", "");
            }
            finally
            {
                done();
            }
        });
    }

}
