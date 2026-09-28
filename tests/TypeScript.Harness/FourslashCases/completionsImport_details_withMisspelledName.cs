// ported from: internal/fourslash/tests/completionsImport_details_withMisspelledName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_details_withMisspelledNameCase
{
    public static void TestCompletionsImport_details_withMisspelledName(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport const abc = 0;\n// @Filename: /b.ts\nacb/*1*/;\n// @Filename: /c.ts\nacb/*2*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyApplyCodeActionFromCompletion(t, "1", new F.ApplyCodeActionFromCompletionOptions { Name = "abc", Source = "./a", Description = "Add import from \"./a\"", NewFileContent = "import { abc } from \"./a\";\n\nacb;" });
                f.GoToMarker(t, "2");
                f.VerifyApplyCodeActionFromCompletion(t, "2", new F.ApplyCodeActionFromCompletionOptions { Name = "abc", Source = "./a", AutoImportFix = new LSP.AutoImportFix { ModuleSpecifier = "./a" }, Description = "Add import from \"./a\"", NewFileContent = "import { abc } from \"./a\";\n\nacb;" });
            }
            finally
            {
                done();
            }
        });
    }

}
