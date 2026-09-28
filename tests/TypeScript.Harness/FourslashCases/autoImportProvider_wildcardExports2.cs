// ported from: internal/fourslash/tests/autoImportProvider_wildcardExports2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_wildcardExports2Case
{
    public static void TestAutoImportProvider_wildcardExports2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{\n    \"name\": \"pkg\",\n    \"version\": \"1.0.0\",\n    \"exports\": {\n        \"./core/*\": {\n            \"types\": \"./lib/core/*.d.ts\",\n            \"default\": \"./lib/core/*.js\"\n        }\n    }\n}\n// @Filename: /home/src/workspaces/project/node_modules/pkg/lib/core/test.d.ts\nexport function test(): void;\n// @Filename: /home/src/workspaces/project/package.json\n{\n    \"type\": \"module\",\n    \"dependencies\": {\n        \"pkg\": \"1.0.0\"\n    }\n}\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"nodenext\",\n        \"lib\": [\"es5\"]\n    }\n}\n// @Filename: /home/src/workspaces/project/main.ts\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "test", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "pkg/core/test" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
