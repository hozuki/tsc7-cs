// ported from: internal/fourslash/tests/autoImportFileExcludePatterns2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportFileExcludePatterns2Case
{
    public static void TestAutoImportFileExcludePatterns2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /lib/components/button/Button.ts\nexport function Button() {}\n// @Filename: /lib/components/button/index.ts\nexport * from \"./Button\";\n// @Filename: /lib/components/index.ts\nexport * from \"./button\";\n// @Filename: /lib/main.ts\nexport { Button } from \"./components\";\n// @Filename: /lib/index.ts\nexport * from \"./main\";\n// @Filename: /i-hate-index-files.ts\nButton/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus([new LSP.CompletionItem { Label = "Button", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./lib/main" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], false) }, UserPreferences = new LSUP.UserPreferences { AutoImportFileExcludePatterns = ["/**/index.*"] } });
                f.VerifyImportFixModuleSpecifiers(t, "", ["./lib/main", "./lib/components/button/Button"], new LSUP.UserPreferences { AutoImportFileExcludePatterns = ["/**/index.*"] });
            }
            finally
            {
                done();
            }
        });
    }

}
