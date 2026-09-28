// ported from: internal/fourslash/tests/completionsImport_ofAlias_preferShortPath_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_ofAlias_preferShortPathCase
{
    public static void TestCompletionsImport_ofAlias_preferShortPath(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonJs\n// @noLib: true\n// @Filename: /foo/index.ts\nexport { foo } from \"./lib/foo\";\n// @Filename: /foo/lib/foo.ts\nexport const foo = 0;\n// @Filename: /user.ts\nfo/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus([new LSP.CompletionItem { Label = "foo", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./foo" } }, Detail = "(alias) const foo: 0\nexport foo", Kind = LSP.CompletionItemKind.Variable, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], true) } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "foo", Source = "./foo", Description = "Add import from \"./foo\"", NewFileContent = "import { foo } from \"./foo\";\n\nfo" });
            }
            finally
            {
                done();
            }
        });
    }

}
