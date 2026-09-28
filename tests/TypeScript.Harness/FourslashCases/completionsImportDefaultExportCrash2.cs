// ported from: internal/fourslash/tests/completionsImportDefaultExportCrash2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImportDefaultExportCrash2Case
{
    public static void TestCompletionsImportDefaultExportCrash2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @allowJs: true\n// @Filename: /node_modules/dom7/index.d.ts\nexport interface Dom7Array {\n  length: number;\n  prop(propName: string): any;\n}\n\nexport interface Dom7 {\n  (): Dom7Array;\n  fn: any;\n}\n\ndeclare const Dom7: Dom7;\n\nexport {\n  Dom7 as $,\n};\n// @Filename: /dom7.js\nimport * as methods from 'dom7';\nObject.keys(methods).forEach((methodName) => {\n  if (methodName === '$') return;\n  methods.$.fn[methodName] = methods[methodName];\n});\n\nexport default methods.$;\n// @Filename: /swipe-back.js\n/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "$", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "dom7" } }, SortText = LS.SortTextAutoImportSuggestions }, new LSP.CompletionItem { Label = "Dom7", AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./dom7" } }, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
