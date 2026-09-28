// ported from: internal/fourslash/tests/completionsOverridingMethod8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethod8Case
{
    public static void TestCompletionsOverridingMethod8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: /types1.ts\nexport interface I { foo: string }\n// @Filename: /types2.ts\nimport { I } from \"./types1\";\nexport interface Base { method(p: I): void }\n// @Filename: /index.ts\nimport { Base } from \"./types2\";\nexport class C implements Base {\n  /**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "method", InsertText = "method(p: I): void {\n}", FilterText = "method", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue }, Name = "method", Source = "ClassMemberSnippet/", Description = "Includes imports of types referenced by 'method'", NewFileContent = "import { I } from \"./types1\";\nimport { Base } from \"./types2\";\nexport class C implements Base {\n  \n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
