// ported from: internal/fourslash/tests/syntheticImportFromBabelGeneratedFile1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SyntheticImportFromBabelGeneratedFile1Case
{
    public static void TestSyntheticImportFromBabelGeneratedFile1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @allowSyntheticDefaultImports: true\n// @Filename: /a.js\nexports.__esModule = true;\nexports.default = f;\n/**\n * Run this function\n * @param {string} t\n */\nfunction f(t) {}\n// @Filename: /b.js\nimport f from \"./a\"\n/**/f"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "(alias) function f(t: string): void\nimport f", "Run this function");
            }
            finally
            {
                done();
            }
        });
    }

}
