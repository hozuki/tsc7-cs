// ported from: internal/fourslash/tests/autoImportSpecifierExcludeRegexes1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportSpecifierExcludeRegexes1Case
{
    public static void TestAutoImportSpecifierExcludeRegexes1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: preserve\n// @Filename: /node_modules/lib/index.d.ts\ndeclare module \"ambient\" {\n    export const x: number;\n}\ndeclare module \"ambient/utils\" {\n   export const x: number;\n}\n// @Filename: /index.ts\nx/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient", "ambient/utils"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["utils"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient", "ambient/utils"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["/UTILS/"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["/UTILS/i"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient", "ambient/utils"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["/ambient/utils/"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["/ambient\\/utils/"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["/.*?$"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["^ambient/"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient/utils"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["ambient$"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["ambient", "ambient/utils"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["oops("] });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "x", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "ambient" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "x", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "ambient/utils" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["ambient/utils"] }, UserPreferences = new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["utils"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
