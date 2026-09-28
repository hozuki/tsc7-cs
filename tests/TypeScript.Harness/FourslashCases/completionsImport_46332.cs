// ported from: internal/fourslash/tests/completionsImport_46332_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_46332Case
{
    public static void TestCompletionsImport_46332(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @moduleResolution: bundler\n// @Filename: /node_modules/vue/package.json\n{\n  \"name\": \"vue\",\n  \"types\": \"dist/vue.d.ts\"\n}\n// @Filename: /node_modules/vue/dist/vue.d.ts\nexport * from \"@vue/runtime-dom\"\n// @Filename: /node_modules/@vue/runtime-dom/package.json\n{\n  \"name\": \"@vue/runtime-dom\",\n  \"types\": \"dist/runtime-dom.d.ts\"\n}\n// @Filename: /node_modules/@vue/runtime-dom/dist/runtime-dom.d.ts\nexport * from \"@vue/runtime-core\";\nexport {}\ndeclare module '@vue/reactivity' {\n  export interface RefUnwrapBailTypes {\n    runtimeDOMBailTypes: any\n  }\n}\n// @Filename: /node_modules/@vue/runtime-core/package.json\n{\n  \"name\": \"@vue/runtime-core\",\n  \"types\": \"dist/runtime-core.d.ts\"\n}\n// @Filename: /node_modules/@vue/runtime-core/dist/runtime-core.d.ts\nimport { ref } from '@vue/reactivity';\nexport { ref };\ndeclare module '@vue/reactivity' {\n  export interface RefUnwrapBailTypes {\n    runtimeCoreBailTypes: any\n  }\n}\n// @Filename: /node_modules/@vue/reactivity/package.json\n{\n  \"name\": \"@vue/reactivity\",\n  \"types\": \"dist/reactivity.d.ts\"\n}\n// @Filename: /node_modules/@vue/reactivity/dist/reactivity.d.ts\nexport declare function ref<T = any>(): T;\n// @Filename: /package.json\n{\n  \"dependencies\": {\n    \"vue\": \"*\"\n  }\n}\n// @Filename: /index.ts\nimport {} from \"vue\";\nref/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "ref", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "vue" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "ref", Source = "vue", Description = "Update import from \"vue\"", AutoImportFix = new LSP.AutoImportFix {  }, UserPreferences = null!, NewFileContent = "import { ref } from \"vue\";\nref" });
            }
            finally
            {
                done();
            }
        });
    }

}
