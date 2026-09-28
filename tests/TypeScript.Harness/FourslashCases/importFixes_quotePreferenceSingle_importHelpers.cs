// ported from: internal/fourslash/tests/importFixes_quotePreferenceSingle_importHelpers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportFixes_quotePreferenceSingle_importHelpersCase
{
    public static void TestImportFixes_quotePreferenceSingle_importHelpers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @importHelpers: true\n// @filename: /a.ts\nexport default () => {};\n// @filename: /b.ts\nexport default () => {};\n// @filename: /test.ts\nimport a from './a';\n[|b|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/test.ts");
                f.VerifyImportFixAtPosition(t, ["import b from './b';\nb"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
