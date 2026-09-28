// ported from: internal/fourslash/tests/completionsImportPathsConflict_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImportPathsConflictCase
{
    public static void TestCompletionsImportPathsConflict(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"esnext\",\n        \"paths\": {\n          \"@reduxjs/toolkit\": [\"src/index.ts\"],\n          \"@internal/*\": [\"src/*\"]\n        }\n    }\n}\n// @Filename: /src/index.ts\nexport { configureStore } from \"./configureStore\";\n// @Filename: /src/configureStore.ts\nexport function configureStore() {}\n// @Filename: /src/tests/createAsyncThunk.typetest.ts\nimport {} from \"@reduxjs/toolkit\";\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "configureStore", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "@reduxjs/toolkit" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "configureStore", Source = "@reduxjs/toolkit", AutoImportFix = new LSP.AutoImportFix { ModuleSpecifier = "@reduxjs/toolkit" }, Description = "Update import from \"@reduxjs/toolkit\"", NewFileContent = "import { configureStore } from \"@reduxjs/toolkit\";\n" });
            }
            finally
            {
                done();
            }
        });
    }

}
