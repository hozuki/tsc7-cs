// ported from: internal/fourslash/tests/importNameCodeFixExportAsDefaultExistingImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixExportAsDefaultExistingImportCase
{
    public static void TestImportNameCodeFixExportAsDefaultExistingImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("import [|{ v1, v2, v3 }|] from \"./module\";\nv4/*0*/();\n// @Filename: module.ts\nconst v4 = 5;\nexport { v4 as default };\nexport const v1 = 5;\nexport const v2 = 5;\nexport const v3 = 5;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["v4, { v1, v2, v3 }"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
