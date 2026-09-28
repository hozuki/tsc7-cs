// ported from: internal/fourslash/tests/completionsImport_mergedReExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_mergedReExportCase
{
    public static void TestCompletionsImport_mergedReExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/package.json\n{ \"dependencies\": { \"@jest/types\": \"*\", \"ts-jest\": \"*\" } }\n// @Filename: /home/src/workspaces/project/node_modules/@jest/types/package.json\n{ \"name\": \"@jest/types\" }\n// @Filename: /home/src/workspaces/project/node_modules/@jest/types/index.d.ts\nimport type * as Config from \"./Config\";\nexport type { Config };\n// @Filename: /home/src/workspaces/project/node_modules/@jest/types/Config.d.ts\nexport interface ConfigGlobals {\n    [K: string]: unknown;\n}\n// @Filename: /home/src/workspaces/project/node_modules/ts-jest/index.d.ts\nexport {};\ndeclare module \"@jest/types\" {\n    namespace Config {\n        interface ConfigGlobals {\n            'ts-jest': any;\n        }\n    }\n}\n// @Filename: /home/src/workspaces/project/index.ts\nC/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Config", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "@jest/types" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.Insert(t, "o");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Config", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "@jest/types" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
