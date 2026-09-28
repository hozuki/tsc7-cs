// ported from: internal/fourslash/tests/completionsUniqueSymbol_import_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsUniqueSymbol_importCase
{
    public static void TestCompletionsUniqueSymbol_import(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n// @Filename: /globals.d.ts\ndeclare const Symbol: () => symbol;\n// @Filename: /a.ts\nconst privateSym = Symbol();\nexport const publicSym = Symbol();\nexport interface I {\n    [privateSym]: number;\n    [publicSym]: number;\n    [defaultPublicSym]: number;\n    n: number;\n}\nexport const i: I;\n// @Filename: /user.ts\nimport { i } from \"./a\";\ni[|./**/|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["n", new LSP.CompletionItem { Label = "publicSym", InsertText = "[publicSym]", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./a" } }, SortText = LS.SortTextGlobalsOrKeywords, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "[publicSym]", Range = f.Ranges()[0].LSRange } } }] } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "publicSym", Source = "./a", Description = "Update import from \"./a\"", NewFileContent = "import { i, publicSym } from \"./a\";\ni.;" });
            }
            finally
            {
                done();
            }
        });
    }

}
