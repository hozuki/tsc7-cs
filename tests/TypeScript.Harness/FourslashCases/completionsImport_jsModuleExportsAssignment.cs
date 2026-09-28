// ported from: internal/fourslash/tests/completionsImport_jsModuleExportsAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_jsModuleExportsAssignmentCase
{
    public static void TestCompletionsImport_jsModuleExportsAssignment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"allowJs\": true, \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/third_party/marked/src/defaults.js\nfunction getDefaults() {\n  return {\n    baseUrl: null,\n  };\n}\n\nfunction changeDefaults(newDefaults) {\n  module.exports.defaults = newDefaults;\n}\n\nmodule.exports = {\n  defaults: getDefaults(),\n  getDefaults,\n  changeDefaults\n};\n// @Filename: /home/src/workspaces/project/index.ts\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                var opts666 = f.GetOptions();
                opts666!.FormatCodeSettings!.NewLineCharacter = "\n";
                f.Configure(t, opts666);
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
                f.Insert(t, "d");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "defaults", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./third_party/marked/src/defaults" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], Excludes = ["newDefaults"] } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "defaults", Source = "./third_party/marked/src/defaults", Description = "Add import from \"./third_party/marked/src/defaults\"", AutoImportFix = new LSP.AutoImportFix { ModuleSpecifier = "./third_party/marked/src/defaults" }, NewFileContent = "import { defaults } from \"./third_party/marked/src/defaults\";\n\nd" });
            }
            finally
            {
                done();
            }
        });
    }

}
