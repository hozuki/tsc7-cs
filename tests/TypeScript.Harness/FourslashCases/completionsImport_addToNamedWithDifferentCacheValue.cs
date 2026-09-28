// ported from: internal/fourslash/tests/completionsImport_addToNamedWithDifferentCacheValue_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_addToNamedWithDifferentCacheValueCase
{
    public static void TestCompletionsImport_addToNamedWithDifferentCacheValue(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/packages/mylib/package.json\n{ \"name\": \"mylib\", \"version\": \"1.0.0\", \"main\": \"index.js\" }\n// @Filename: /home/src/workspaces/project/packages/mylib/index.ts\nexport * from \"./mySubDir\";\n// @Filename: /home/src/workspaces/project/packages/mylib/mySubDir/index.ts\nexport * from \"./myClass\";\nexport * from \"./myClass2\";\n// @Filename: /home/src/workspaces/project/packages/mylib/mySubDir/myClass.ts\nexport class MyClass {}\n// @Filename: /home/src/workspaces/project/packages/mylib/mySubDir/myClass2.ts\nexport class MyClass2 {}\n// @link: /home/src/workspaces/project/packages/mylib -> /home/src/workspaces/project/node_modules/mylib\n// @Filename: /home/src/workspaces/project/src/index.ts\n\nconst a = new MyClass/*1*/();\nconst b = new MyClass2/*2*/();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "1");
                var opts1267 = f.GetOptions();
                opts1267!.FormatCodeSettings!.NewLineCharacter = "\n";
                f.Configure(t, opts1267);
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "MyClass", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "../packages/mylib" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyApplyCodeActionFromCompletion(t, "1", new F.ApplyCodeActionFromCompletionOptions { Name = "MyClass", Source = "../packages/mylib", Description = "Add import from \"../packages/mylib\"", AutoImportFix = new LSP.AutoImportFix {  }, UserPreferences = null!, NewFileContent = "import { MyClass } from \"../packages/mylib\";\n\nconst a = new MyClass();\nconst b = new MyClass2();" });
                f.ReplaceLine(t, 0, "import { MyClass } from \"mylib\";");
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "MyClass2", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "mylib" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
