// ported from: internal/fourslash/tests/completionsImport_default_symbolName_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_default_symbolNameCase
{
    public static void TestCompletionsImport_default_symbolName(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @esModuleInterop: false\n// @allowSyntheticDefaultImports: false\n// @Filename: /node_modules/@types/range-parser/index.d.ts\ndeclare function RangeParser(): string;\ndeclare namespace RangeParser {\n    interface Options {\n        combine?: boolean;\n    }\n}\nexport = RangeParser;\n// @Filename: /b.ts\nR/*0*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "RangeParser", Kind = LSP.CompletionItemKind.Function, Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "range-parser" } }, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions, Detail = "namespace RangeParser\nfunction RangeParser(): string" }] } });
                f.VerifyApplyCodeActionFromCompletion(t, "0", new F.ApplyCodeActionFromCompletionOptions { Name = "RangeParser", Source = "range-parser", Description = "Add import from \"range-parser\"", NewFileContent = "import RangeParser = require(\"range-parser\");\n\nR" });
            }
            finally
            {
                done();
            }
        });
    }

}
