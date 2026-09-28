// ported from: internal/fourslash/tests/completionsImport_importType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_importTypeCase
{
    public static void TestCompletionsImport_importType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\nexport const x = 0;\nexport class C {}\n/** @typedef {number} T */\n// @Filename: /b.js\nexport const m = 0;\n/** @type {/*0*/} */\n/** @type {/*1*/} */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["0", "1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "C", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./a" } }, Detail = "class C", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "T", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./a" } }, Detail = "type T = number", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], Excludes = ["x"] } });
                f.VerifyApplyCodeActionFromCompletion(t, "0", new F.ApplyCodeActionFromCompletionOptions { Name = "C", Source = "./a", Description = "Add import from \"./a\"", NewFileContent = "import { C } from \"./a\";\n\nexport const m = 0;\n/** @type {} */\n/** @type {} */" });
                f.VerifyApplyCodeActionFromCompletion(t, "1", new F.ApplyCodeActionFromCompletionOptions { Name = "T", Source = "./a", Description = "Change 'T' to 'import(\"./a\").T'", NewFileContent = "import { C } from \"./a\";\n\nexport const m = 0;\n/** @type {} */\n/** @type {import(\"./a\").} */" });
            }
            finally
            {
                done();
            }
        });
    }

}
