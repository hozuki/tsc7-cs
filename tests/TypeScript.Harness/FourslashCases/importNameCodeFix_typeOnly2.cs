// ported from: internal/fourslash/tests/importNameCodeFix_typeOnly2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_typeOnly2Case
{
    public static void TestImportNameCodeFix_typeOnly2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @importsNotUsedAsValues: error\n// @Filename: types.ts\nexport class A {}\n// @Filename: index.ts\nconst a: A = new A();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "index.ts");
                f.VerifyCodeFixAll(t, new F.VerifyCodeFixAllOptions { FixID = "fixMissingImport", NewFileContent = "import { A } from \"./types\";\n\nconst a: A = new A();" });
            }
            finally
            {
                done();
            }
        });
    }

}
