// ported from: internal/fourslash/tests/completionsImport_preferUpdatingExistingImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_preferUpdatingExistingImportCase
{
    public static void TestCompletionsImport_preferUpdatingExistingImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: commonjs\n// @Filename: /deep/module/why/you/want/this/path.ts\nexport const x = 0;\nexport const y = 1;\n// @Filename: /nice/reexport.ts\nimport { x, y } from \"../deep/module/why/you/want/this/path\";\nexport { x, y };\n// @Filename: /index.ts\nimport { x } from \"./deep/module/why/you/want/this/path\";\n\ny/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalsPlus(["x", new LSP.CompletionItem { Label = "y", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./deep/module/why/you/want/this/path" } }, SortText = LS.SortTextAutoImportSuggestions, AdditionalTextEdits = F.FourslashTest.AnyTextEdits }], false) } });
            }
            finally
            {
                done();
            }
        });
    }

}
