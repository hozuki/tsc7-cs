// ported from: internal/fourslash/tests/importNameCodeFixExportAsDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixExportAsDefaultCase
{
    public static void TestImportNameCodeFixExportAsDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /foo.ts\nconst foo = 'foo'\nexport { foo as default }\n// @Filename: /index.ts\n foo/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "foo", Source = "./foo", Description = "Add import from \"./foo\"", NewFileContent = "import foo from \"./foo\";\n\nfoo", UserPreferences = null! });
            }
            finally
            {
                done();
            }
        });
    }

}
