// ported from: internal/fourslash/tests/autoImportNodeNextJSRequire_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportNodeNextJSRequireCase
{
    public static void TestAutoImportNodeNextJSRequire(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @allowJs: true\n// @checkJs: true\n// @noEmit: true\n// @Filename: /matrix.js\nexports.variants = [];\n// @Filename: /main.js\nexports.dedupeLines = data => {\n  variants/**/\n}\n// @Filename: /totally-irrelevant-no-way-this-changes-things-right.js\nexport default 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/main.js");
                f.VerifyImportFixAtPosition(t, ["const { variants } = require(\"./matrix\")\n\nexports.dedupeLines = data => {\n  variants\n}"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
