// ported from: internal/fourslash/tests/importTypeCompletions7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportTypeCompletions7Case
{
    public static void TestImportTypeCompletions7(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: es2020\n// @module: esnext\n// @Filename: /foo.d.ts\ndeclare namespace Foo {}\nexport = Foo;\n// @Filename: /test.ts\n[|import F/**/|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/test.ts");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "Foo", InsertText = "import Foo from \"./foo\";", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./foo" } }, TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "import Foo from \"./foo\";", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
