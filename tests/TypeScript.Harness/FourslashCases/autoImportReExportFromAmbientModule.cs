// ported from: internal/fourslash/tests/autoImportReExportFromAmbientModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportReExportFromAmbientModuleCase
{
    public static void TestAutoImportReExportFromAmbientModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"commonjs\",\n    \"types\": [\"*\"],\n    \"lib\": [\"es5\"]\n  }\n}\n// @Filename: /home/src/workspaces/project/node_modules/@types/node/index.d.ts\ndeclare module \"fs\" {\n  export function accessSync(path: string): void;\n}\n// @Filename: /home/src/workspaces/project/node_modules/@types/fs-extra/index.d.ts\nexport * from \"fs\";\n// @Filename: /home/src/workspaces/project/index.ts\naccess/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "accessSync", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "fs" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "accessSync", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "fs-extra" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "accessSync", Source = "fs-extra", Description = "Add import from \"fs-extra\"", NewFileContent = "import { accessSync } from \"fs-extra\";\n\naccess", AutoImportFix = new LSP.AutoImportFix { ModuleSpecifier = "fs-extra" } });
            }
            finally
            {
                done();
            }
        });
    }

}
