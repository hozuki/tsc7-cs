// ported from: internal/fourslash/tests/importSuggestionsCache_invalidPackageJson_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportSuggestionsCache_invalidPackageJsonCase
{
    public static void TestImportSuggestionsCache_invalidPackageJson(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /home/src/workspaces/project/jsconfig.json\n{\n  \"compilerOptions\": {\n    \"lib\": [\"es5\"],\n    \"module\": \"commonjs\",\n    \"types\": [\"*\"]\n  },\n}\n// @Filename: /home/src/workspaces/project/node_modules/@types/node/index.d.ts\ndeclare module 'fs' {\n  export function readFile(): void;\n}\ndeclare module 'util' {\n  export function promisify(): void;\n}\n// @Filename: /home/src/workspaces/project/package.json\n{ \"mod\" }\n// @Filename: /home/src/workspaces/project/a.js\n\nreadF/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "readFile", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "fs" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
