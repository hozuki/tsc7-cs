// ported from: internal/fourslash/tests/importNameCodeFix_commonjs_allowSynthetic_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_commonjs_allowSyntheticCase
{
    public static void TestImportNameCodeFix_commonjs_allowSynthetic(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @moduleResolution: bundler\n// @allowJs: true\n// @checkJs: true\n// @allowSyntheticDefaultImports: true\n// @Filename: /test_module.js\nconst MY_EXPORTS = {}\nmodule.exports = MY_EXPORTS;\n// @Filename: /index.js\nconst newVar = {\n  any: MY_EXPORTS/**/,\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["const MY_EXPORTS = require(\"./test_module\");\n\nconst newVar = {\n  any: MY_EXPORTS,\n}"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
