// ported from: internal/fourslash/tests/completionsImport_default_reExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_default_reExportCase
{
    public static void TestCompletionsImport_default_reExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: commonjs\n// @allowJs: true\n// @Filename: /file1.js\nconst a = 1;\nexport {\n    a as b\n};\nexport default a;\n// @Filename: /file2.js\nimport * as foo from './file1';\n/**/\nexport default foo.b;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsInJSPlus(["foo", new LSP.CompletionItem { Label = "a", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./file1" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "b", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./file1" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], false) } });
            }
            finally
            {
                done();
            }
        });
    }

}
