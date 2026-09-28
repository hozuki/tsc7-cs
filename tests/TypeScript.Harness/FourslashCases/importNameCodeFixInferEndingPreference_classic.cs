// ported from: internal/fourslash/tests/importNameCodeFixInferEndingPreference_classic_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixInferEndingPreference_classicCase
{
    public static void TestImportNameCodeFixInferEndingPreference_classic(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @checkJs: true\n// @allowJs: true\n// @noEmit: true\n// @Filename: /a.js\nexport const a = 0;\n// @Filename: /b.js\nexport const b = 0;\n// @Filename: /c.js\nimport { a } from \"./a.js\";\n\nb/**/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["./b.js"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
