// ported from: internal/fourslash/tests/completionsImport_compilerOptionsModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_compilerOptionsModuleCase
{
    public static void TestCompletionsImport_compilerOptionsModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @module: commonjs\n// @Filename: /node_modules/a/index.d.ts\nexport const foo = 0;\n// @Filename: /b.js\nconst a = require(\"./a\");\nfo/*b*/\n// @Filename: /c.js\nconst x = 0;/*c*/\n// @Filename: /c1.js\n// @ts-check\nconst x = 0;/*ccheck*/\n// @Filename: /c2.ts\nconst x = 0;/*cts*/\n// @Filename: /d.js\nconst a = import(\"./a\"); // Does not make this an external module\nfo/*d*/\n// @Filename: /d1.js\n// @ts-check\nconst a = import(\"./a\"); // Does not make this an external module\nfo/*dcheck*/\n// @Filename: /d2.ts\nconst a = import(\"./a\"); // Does not make this an external module\nfo/*dts*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["b", "c", "ccheck", "cts", "d", "dcheck", "dts"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "a" } }, Detail = "const foo: 0", Kind = LSP.CompletionItemKind.Variable, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, SortText = LS.SortTextAutoImportSuggestions }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
