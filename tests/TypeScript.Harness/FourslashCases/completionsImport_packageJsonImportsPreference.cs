// ported from: internal/fourslash/tests/completionsImport_packageJsonImportsPreference_test.go
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

internal static class CompletionsImport_packageJsonImportsPreferenceCase
{
    public static void TestCompletionsImport_packageJsonImportsPreference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: preserve\n// @allowImportingTsExtensions: true\n// @Filename: /project/package.json\n{\n  \"name\": \"project\",\n  \"version\": \"1.0.0\",\n  \"imports\": {\n    \"#internal/*\": \"./src/internal/*.ts\"\n  }\n}\n// @Filename: /project/src/internal/foo.ts\nexport const internalFoo = 0;\n// @Filename: /project/src/other.ts\nexport * from \"./internal/foo.ts\";\n// @Filename: /project/src/main.ts\ninternalFoo/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "internalFoo", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "#internal/foo" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative") } });
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "internalFoo", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./other" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] }, UserPreferences = new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("relative") } });
            }
            finally
            {
                done();
            }
        });
    }

}
