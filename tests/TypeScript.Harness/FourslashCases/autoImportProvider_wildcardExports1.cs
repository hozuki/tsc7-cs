// ported from: internal/fourslash/tests/autoImportProvider_wildcardExports1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_wildcardExports1Case
{
    public static void TestAutoImportProvider_wildcardExports1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{\n    \"name\": \"pkg\",\n    \"version\": \"1.0.0\",\n    \"exports\": {\n        \"./*\": \"./a/*.js\",\n        \"./b/*.js\": \"./b/*.js\",\n        \"./c/*\": \"./c/*\",\n        \"./d/*\": {\n            \"import\": \"./d/*.mjs\"\n        }\n    }\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/a/a1.d.ts\nexport const a1: number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/b/b1.d.ts\nexport const b1: number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/b/b2.d.mts\nexport const NOT_REACHABLE: number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/c/c1.d.ts\nexport const c1: number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/c/subfolder/c2.d.mts\nexport const c2: number;\n// @Filename: /home/src/workspaces/project/node_modules/pkg/d/d1.d.mts\nexport const d1: number;\n// @Filename: /home/src/workspaces/project/package.json\n{\n    \"type\": \"module\",\n    \"dependencies\": {\n        \"pkg\": \"1.0.0\"\n    }\n}\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"nodenext\",\n        \"lib\": [\"es5\"]\n    }\n}\n// @Filename: /home/src/workspaces/project/main.ts\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a1", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "pkg/a1" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "b1", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "pkg/b/b1.js" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "c1", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "pkg/c/c1.js" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "c2", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "pkg/c/subfolder/c2.mjs" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "d1", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "pkg/d/d1" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], Excludes = ["NOT_REACHABLE"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
