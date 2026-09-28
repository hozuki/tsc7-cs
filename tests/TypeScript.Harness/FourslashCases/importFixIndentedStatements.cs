// ported from: internal/fourslash/tests/importFixIndentedStatements_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportFixIndentedStatementsCase
{
    public static void TestImportFixBeforeIndentedImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var (f, done) = F.Fourslash.NewFourslashWithOptions(t, "// @Filename: /aaa.ts\nexport const helper = 1;\n\n// @Filename: /dep.ts\nexport const existing = 2;\n\n// @Filename: /main.ts\n// header\n  import { existing } from \"./dep\";\n  const value = help/**/;\n", new F.FourslashOptions {  });
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "helper", Source = "./aaa", Description = "Add import from \"./aaa\"", NewFileContent = "// header\n  import { helper } from \"./aaa\";\n  import { existing } from \"./dep\";\n  const value = help;\n" });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportFixAfterIndentedImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var (f, done) = F.Fourslash.NewFourslashWithOptions(t, "// @Filename: /aaa.ts\nexport const existing = 2;\n\n// @Filename: /zzz.ts\nexport const helper = 1;\n\n// @Filename: /main.ts\n// header\n  import { existing } from \"./aaa\";\n  const value = help/**/;\n", new F.FourslashOptions {  });
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "helper", Source = "./zzz", Description = "Add import from \"./zzz\"", NewFileContent = "// header\n  import { existing } from \"./aaa\";\n  import { helper } from \"./zzz\";\n  const value = help;\n" });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportFixBeforeIndentedImportWithCarriageReturns(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var (f, done) = F.Fourslash.NewFourslashWithOptions(t, "// @Filename: /aaa.ts\nexport const helper = 1;\n\n// @Filename: /dep.ts\nexport const existing = 2;\n\n// @Filename: /main.ts\n" + "// header\r  import { existing } from \"./dep\";\r  const value = help/**/;\r", new F.FourslashOptions {  });
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "helper", Source = "./aaa", Description = "Add import from \"./aaa\"", NewFileContent = "// header\r  import { helper } from \"./aaa\";\n  import { existing } from \"./dep\";\r  const value = help;\r" });
            }
            finally
            {
                done();
            }
        });
    }

}
