// ported from: internal/fourslash/tests/autoImportPathsAliasesAndBarrels_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPathsAliasesAndBarrelsCase
{
    public static void TestAutoImportPathsAliasesAndBarrels(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n {\n  \"compilerOptions\": {\n    \"module\": \"commonjs\",\n    \"paths\": {\n      \"~/*\": [\"src/*\"]  \n    }\n  }\n}\n// @Filename: /src/dirA/index.ts\n export * from \"./thing1A\";\n export * from \"./thing2A\";\n// @Filename: /src/dirA/thing1A.ts\n export class Thing1A {}\n Thing/**/\n// @Filename: /src/dirA/thing2A.ts\n export class Thing2A {}\n// @Filename: /src/dirB/index.ts\n export * from \"./thing1B\";\n export * from \"./thing2B\";\n// @Filename: /src/dirB/thing1B.ts\n export class Thing1B {}\n// @Filename: /src/dirB/thing2B.ts\n export class Thing2B {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Thing2A", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./thing2A" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "Thing1B", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "~/dirB" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "Thing2B", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "~/dirB" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
