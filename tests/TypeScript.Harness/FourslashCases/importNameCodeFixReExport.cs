// ported from: internal/fourslash/tests/importNameCodeFixReExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixReExportCase
{
    public static void TestImportNameCodeFixReExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport const x = 0\";\n// @Filename: /b.ts\n[|export { x } from \"./a\";\nx;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.ts");
                f.VerifyRangeAfterCodeFix(t, "import { x } from \"./a\";\n\nexport { x } from \"./a\";\nx;", true, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
