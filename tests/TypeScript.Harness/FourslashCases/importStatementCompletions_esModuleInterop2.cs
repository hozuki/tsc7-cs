// ported from: internal/fourslash/tests/importStatementCompletions_esModuleInterop2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportStatementCompletions_esModuleInterop2Case
{
    public static void TestImportStatementCompletions_esModuleInterop2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @esModuleInterop: true\n// @Filename: /mod.ts\nconst foo = 0;\nexport = foo;\n// @Filename: /importExportEquals.ts\n[|import f/**/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "foo", InsertText = "import foo$1 from \"./mod\";", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./mod" } }, InsertTextFormat = LSP.InsertTextFormat.Snippet, TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "import foo$1 from \"./mod\";", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
