// ported from: internal/fourslash/tests/autoImportProvider_globalTypingsCache_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_globalTypingsCacheCase
{
    public static void TestAutoImportProvider_globalTypingsCache(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/Library/Caches/typescript/node_modules/@types/react-router-dom/package.json\n { \"name\": \"@types/react-router-dom\", \"version\": \"16.8.4\", \"types\": \"index.d.ts\" }\n// @Filename: /home/src/Library/Caches/typescript/node_modules/@types/react-router-dom/index.d.ts\n export class BrowserRouterFromDts {}\n// @Filename: /home/src/workspaces/project/package.json\n { \"dependencies\": { \"react-router-dom\": \"*\" } }\n// @Filename: /home/src/workspaces/project/tsconfig.json\n { \"compilerOptions\": { \"module\": \"commonjs\", \"lib\": [\"es5\"], \"allowJs\": true, \"checkJs\": true, \"maxNodeModuleJsDepth\": 2 }, \"typeAcquisition\": { \"enable\": true } }\n// @Filename: /home/src/workspaces/project/node_modules/react-router-dom/package.json\n { \"name\": \"react-router-dom\", \"version\": \"16.8.4\", \"main\": \"index.js\" }\n// @Filename: /home/src/workspaces/project/node_modules/react-router-dom/index.js\n import \"./BrowserRouter\";\n export {};\n// @Filename: /home/src/workspaces/project/node_modules/react-router-dom/BrowserRouter.js\n export const BrowserRouterFromJs = () => null;\n// @Filename: /home/src/workspaces/project/index.js\nBrowserRouter/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsInJSPlus([new LSP.CompletionItem { Label = "BrowserRouterFromDts", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "react-router-dom" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], false) } });
            }
            finally
            {
                done();
            }
        });
    }

}
