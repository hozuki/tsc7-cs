// ported from: internal/fourslash/tests/autoImportProvider_exportMap1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_exportMap1Case
{
    public static void TestAutoImportProvider_exportMap1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"lib\": [\"es5\"],\n    \"module\": \"nodenext\"\n  }\n}\n// @Filename: /home/src/workspaces/project/package.json\n{\n  \"type\": \"module\",\n  \"dependencies\": {\n    \"dependency\": \"^1.0.0\"\n  }\n}\n// @Filename: /home/src/workspaces/project/node_modules/dependency/package.json\n{\n  \"type\": \"module\",\n  \"name\": \"dependency\",\n  \"version\": \"1.0.0\",\n  \"exports\": {\n    \".\": {\n      \"types\": \"./lib/index.d.ts\"\n    },\n    \"./lol\": {\n      \"types\": \"./lib/lol.d.ts\"\n    }\n  }\n}\n// @Filename: /home/src/workspaces/project/node_modules/dependency/lib/index.d.ts\nexport function fooFromIndex(): void;\n// @Filename: /home/src/workspaces/project/node_modules/dependency/lib/lol.d.ts\nexport function fooFromLol(): void;\n// @Filename: /home/src/workspaces/project/src/foo.ts\nfooFrom/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "fooFromIndex", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "dependency" } }, SortText = LS.SortTextAutoImportSuggestions, AdditionalTextEdits = F.FourslashTest.AnyTextEdits }, new LSP.CompletionItem { Label = "fooFromLol", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "dependency/lol" } }, SortText = LS.SortTextAutoImportSuggestions, AdditionalTextEdits = F.FourslashTest.AnyTextEdits }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
