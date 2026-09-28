// ported from: internal/fourslash/tests/completionsImport_ambient_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_ambientCase
{
    public static void TestCompletionsImport_ambient(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: commonjs\n// @Filename: a.d.ts\ndeclare namespace foo { class Bar {} }\ndeclare module 'path1' {\n  import Bar = foo.Bar;\n  export default Bar;\n}\ndeclare module 'path2longer' {\n  import Bar = foo.Bar;\n  export {Bar};\n}\n\n// @Filename: b.ts\nBa/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus([new LSP.CompletionItem { Label = "foo", SortText = LS.SortTextGlobalsOrKeywords }, new LSP.CompletionItem { Label = "Bar", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "path1" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "Bar", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "path2longer" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], false) } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "Bar", Source = "path2longer", Description = "Add import from \"path2longer\"", NewFileContent = "import { Bar } from \"path2longer\";\n\nBa", UserPreferences = null! });
            }
            finally
            {
                done();
            }
        });
    }

}
