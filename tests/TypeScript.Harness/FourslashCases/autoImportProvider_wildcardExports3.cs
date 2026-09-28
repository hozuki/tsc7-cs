// ported from: internal/fourslash/tests/autoImportProvider_wildcardExports3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_wildcardExports3Case
{
    public static void TestAutoImportProvider_wildcardExports3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/packages/ui/package.json\n{\n  \"name\": \"@repo/ui\",\n  \"version\": \"1.0.0\",\n  \"exports\": {\n    \"./*\": \"./src/*.tsx\"\n  }\n}\n// @Filename: /home/src/workspaces/project/packages/ui/src/Card.tsx\nexport const Card = () => null;\n// @Filename: /home/src/workspaces/project/apps/web/package.json\n{\n  \"name\": \"web\",\n  \"version\": \"1.0.0\",\n  \"dependencies\": {\n    \"@repo/ui\": \"workspace:*\"\n  }\n}\n// @Filename: /home/src/workspaces/project/apps/web/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"esnext\",\n    \"moduleResolution\": \"bundler\",\n    \"noEmit\": true,\n    \"jsx\": \"preserve\",\n    \"lib\": [\"es5\"]\n  },\n \"include\": [\"app\"]\n}\n// @Filename: /home/src/workspaces/project/apps/web/app/index.tsx\n(<Card/**/ />);\n// @link: /home/src/workspaces/project/packages/ui -> /home/src/workspaces/project/apps/web/node_modules/@repo/ui"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Card", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "@repo/ui/Card" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
