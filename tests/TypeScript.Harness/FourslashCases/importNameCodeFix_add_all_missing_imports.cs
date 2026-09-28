// ported from: internal/fourslash/tests/importNameCodeFix_add_all_missing_imports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_add_all_missing_importsCase
{
    public static void TestImportNameCodeFix_add_all_missing_imports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport const a: number;\n// @Filename: /b.ts\nexport const b: number;\n// @Filename: /c.ts\nexport const c: number;\n// @Filename: /main.ts\na;\nb;\nc;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/main.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import { a } from \"./a\";\nimport { b } from \"./b\";\nimport { c } from \"./c\";\n\na;\nb;\nc;" });
            }
            finally
            {
                done();
            }
        });
    }

}
