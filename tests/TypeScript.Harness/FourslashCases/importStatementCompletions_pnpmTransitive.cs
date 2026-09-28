// ported from: internal/fourslash/tests/importStatementCompletions_pnpmTransitive_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportStatementCompletions_pnpmTransitiveCase
{
    public static void TestImportStatementCompletions_pnpmTransitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/node_modules/.pnpm/@types+react@17.0.7/node_modules/@types/react/index.d.ts\nimport \"csstype\";\nexport declare function Component(): void;\n// @Filename: /home/src/workspaces/project/node_modules/.pnpm/csstype@3.0.8/node_modules/csstype/index.d.ts\nexport interface SvgProperties {}\n// @Filename: /home/src/workspaces/project/index.ts\n[|import SvgProp/**/|]\n// @link: /home/src/workspaces/project/node_modules/.pnpm/@types+react@17.0.7/node_modules/@types/react -> /home/src/workspaces/project/node_modules/@types/react\n// @link: /home/src/workspaces/project/node_modules/.pnpm/csstype@3.0.8/node_modules/csstype -> /home/src/workspaces/project/node_modules/.pnpm/@types+react@17.0.7/node_modules/csstype"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "type", SortText = LS.SortTextGlobalsOrKeywords }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
