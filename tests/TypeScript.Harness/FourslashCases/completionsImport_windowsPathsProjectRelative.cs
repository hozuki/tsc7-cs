// ported from: internal/fourslash/tests/completionsImport_windowsPathsProjectRelative_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_windowsPathsProjectRelativeCase
{
    public static void TestCompletionsImport_windowsPathsProjectRelative(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: c:/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"paths\": {\n      \"~/noIndex/*\": [\"./src/noIndex/*\"],\n      \"~/withIndex\": [\"./src/withIndex/index.ts\"]\n    }\n  }\n}\n// @Filename: c:/project/package.json\n{}\n// @Filename: c:/project/src/noIndex/a.ts\nexport const myFunctionA = () => {};\n// @Filename: c:/project/src/withIndex/b.ts\nexport const myFunctionB = () => {};\n// @Filename: c:/project/src/withIndex/index.ts\nexport * from './b';\n// @Filename: c:/project/src/reproduction/1.ts\nmyFunction/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "myFunctionA", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "~/noIndex/a" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "myFunctionB", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "~/withIndex" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative") } });
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "myFunctionA", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "../noIndex/a" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "myFunctionB", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "../withIndex" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("relative") } });
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "myFunctionA", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "../noIndex/a" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "myFunctionB", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "../withIndex" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("project-relative") } });
            }
            finally
            {
                done();
            }
        });
    }

}
