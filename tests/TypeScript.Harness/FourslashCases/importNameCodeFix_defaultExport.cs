// ported from: internal/fourslash/tests/importNameCodeFix_defaultExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_defaultExportCase
{
    public static void TestImportNameCodeFix_defaultExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @allowJs: true\n// @checkJs: true\n// @Filename: /a.js\nclass C {}\nexport default C;\n// @Filename: /b.js\n[|C;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.js");
                f.VerifyImportFixAtPosition(t, ["import C from \"./a\";\n\nC;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
