// ported from: internal/fourslash/tests/autoImportFileExcludePatterns3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportFileExcludePatterns3Case
{
    public static void TestAutoImportFileExcludePatterns3(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: commonjs\n// @Filename: /ambient1.d.ts\ndeclare module \"foo\" {\n   export const x = 1;\n}\n// @Filename: /ambient2.d.ts\ndeclare module \"foo\" {\n   export const y = 2;\n}\n// @Filename: /index.ts\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus([new LSP.CompletionItem { Label = "x", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "foo" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "y", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "foo" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], false) }, UserPreferences = new LSUP.UserPreferences { AutoImportFileExcludePatterns = ["/**/ambient1.d.ts"] } });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobals }, UserPreferences = new LSUP.UserPreferences { AutoImportFileExcludePatterns = ["/**/ambient*"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
