// ported from: internal/fourslash/tests/autoImportProvider_namespaceSameNameAsIntrinsic_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_namespaceSameNameAsIntrinsicCase
{
    public static void TestAutoImportProvider_namespaceSameNameAsIntrinsic(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/node_modules/fp-ts/package.json\n{ \"name\": \"fp-ts\", \"version\": \"0.10.4\" }\n// @Filename: /home/src/workspaces/project/node_modules/fp-ts/index.d.ts\nexport * as string from \"./lib/string\";\n// @Filename: /home/src/workspaces/project/node_modules/fp-ts/lib/string.d.ts\nexport declare const fromString: (s: string) => string;\nexport type SafeString = string;\n// @Filename: /home/src/workspaces/project/package.json\n{ \"dependencies\": { \"fp-ts\": \"^0.10.4\" } }\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/index.ts\ntype A = { name: string/**/ }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "string", SortText = LS.SortTextGlobalsOrKeywords }, new LSP.CompletionItem { Label = "string", SortText = LS.SortTextAutoImportSuggestions, Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "fp-ts" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
