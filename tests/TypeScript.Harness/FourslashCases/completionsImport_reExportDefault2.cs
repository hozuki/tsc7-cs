// ported from: internal/fourslash/tests/completionsImport_reExportDefault2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_reExportDefault2Case
{
    public static void TestCompletionsImport_reExportDefault2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: preserve\n// @checkJs: true\n// @Filename: /node_modules/example/package.json\n{ \"name\": \"example\", \"version\": \"1.0.0\", \"main\": \"dist/index.js\" }\n// @Filename: /node_modules/example/dist/nested/module.d.ts\ndeclare const defaultExport: () => void;\ndeclare const namedExport: () => void;\n\nexport default defaultExport;\nexport { namedExport };\n// @Filename: /node_modules/example/dist/index.d.ts\nexport { default, namedExport } from \"./nested/module\";\n// @Filename: /index.mjs\nimport { namedExport } from \"example\";\ndefaultExp/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsInJSPlus(["namedExport", new LSP.CompletionItem { Label = "defaultExport", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "example" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }], false) } });
            }
            finally
            {
                done();
            }
        });
    }

}
