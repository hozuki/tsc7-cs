// ported from: internal/fourslash/tests/completionsOverridingMethod20_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethod20Case
{
    public static void TestCompletionsOverridingMethod20(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n// @newline: LF\nabstract class AFoo {\n    abstract bar(): Promise<void>;\n}\nclass Foo extends AFoo {\n    async [|b/*a*/|]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "bar", TextEdit = U.InsertReplaceTextEdit("async bar(): Promise<void> {\n}", f.Ranges()[0].LSRange), FilterText = "bar", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "a", new F.ApplyCodeActionFromCompletionOptions { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue }, Name = "bar", Source = "ClassMemberSnippet/", Description = "Update modifiers of 'bar'", NewFileContent = "abstract class AFoo {\n    abstract bar(): Promise<void>;\n}\nclass Foo extends AFoo {\n    b\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
