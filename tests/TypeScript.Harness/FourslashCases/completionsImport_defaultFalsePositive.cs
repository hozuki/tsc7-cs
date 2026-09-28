// ported from: internal/fourslash/tests/completionsImport_defaultFalsePositive_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSU = TypeScript.LsUtil.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_defaultFalsePositiveCase
{
    public static void TestCompletionsImport_defaultFalsePositive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/foo/index.ts\nexport default function f(): void;\n// @Filename: /node_modules/bar/concat.d.ts\nexport const concat = 0;\n// @Filename: /a.ts\nexport {};\nconca/**/"u8);
            var prefs = LSU.NewDefaultUserPreferences();
            prefs!.AutoImportEntrypointDirectorySearch = TRI.TSTrue;
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, prefs);
                f.GoToFile(t, "/a.ts");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "concat", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "bar/concat" } }, Detail = "const concat: 0", Kind = LSP.CompletionItemKind.Variable, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "concat", Source = "bar/concat", Description = "Add import from \"bar/concat\"", UserPreferences = prefs, NewFileContent = "import { concat } from \"bar/concat\";\n\nexport {};\nconca" });
            }
            finally
            {
                done();
            }
        });
    }

}
