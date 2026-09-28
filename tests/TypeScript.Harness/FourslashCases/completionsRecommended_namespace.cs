// ported from: internal/fourslash/tests/completionsRecommended_namespace_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsRecommended_namespaceCase
{
    public static void TestCompletionsRecommended_namespace(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n// @Filename: /a.ts\nexport namespace Name {\n    export class C {}\n}\nexport function f(c: Name.C) {}\nf(new N/*a0*/);\nf(new /*a1*/);\n// @Filename: /b.ts\nimport { f } from \"./a\";\nf(new N/*b0*/);\nf(new /*b1*/);\n// @Filename: /c.ts\nimport * as alpha from \"./a\";\nalpha.f(new a/*c0*/);\nalpha.f(new /*c1*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["a0", "a1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Name", Detail = "namespace Name", Kind = LSP.CompletionItemKind.Module, Preselect = true }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["b0", "b1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Name", Data = new LSP.CompletionItemData { AutoImport = new LSP.AutoImportFix { ModuleSpecifier = "./a" } }, Detail = "namespace Name", Kind = LSP.CompletionItemKind.Module, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Preselect = true, SortText = LS.SortTextAutoImportSuggestions }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["c0", "c1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "alpha", Detail = "import alpha", Kind = LSP.CompletionItemKind.Variable, Preselect = true }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
