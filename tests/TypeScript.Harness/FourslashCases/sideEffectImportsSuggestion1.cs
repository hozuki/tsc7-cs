// ported from: internal/fourslash/tests/sideEffectImportsSuggestion1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SideEffectImportsSuggestion1Case
{
    public static void TestSideEffectImportsSuggestion1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @noEmit: true\n// @module: commonjs\n// @noUncheckedSideEffectImports: true\n// @filename: moduleA/a.js\nimport \"b\";\nimport \"c\";\n// @filename: node_modules/b.ts\nvar a = 10;\n// @filename: node_modules/c.js\nexports.a = 10;\nc = 10;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySuggestionDiagnostics(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
