// ported from: internal/fourslash/tests/completionsImport_multipleWithSameName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_multipleWithSameNameCase
{
    public static void TestCompletionsImport_multipleWithSameName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @noLib: true\n// @Filename: /global.d.ts\ndeclare var foo: number;\n// @Filename: /a.ts\nexport const foo = 0;\n// @Filename: /b.ts\nexport const foo = 1;\n// @Filename: /c.ts\nfo/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus([new LSP.CompletionItem { Label = "foo", Detail = "var foo: number", Kind = LSP.CompletionItemKind.Variable, SortText = LS.SortTextGlobalsOrKeywords }, new LSP.CompletionItem { Label = "foo", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./a" } }, Detail = "const foo: 0", Kind = LSP.CompletionItemKind.Variable, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "foo", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./b" } }, Detail = "const foo: 1", Kind = LSP.CompletionItemKind.Variable, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], true) } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "foo", Source = "./b", Description = "Add import from \"./b\"", NewFileContent = "import { foo } from \"./b\";\n\nfo" });
            }
            finally
            {
                done();
            }
        });
    }

}
