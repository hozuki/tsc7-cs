// ported from: internal/fourslash/tests/completionsOverridingMethod6_test.go
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

internal static class CompletionsOverridingMethod6Case
{
    public static void TestCompletionsOverridingMethod6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n// @newline: LF\nclass Base {\n    method() {}\n    protected prop = 1;\n}\nclass A extends Base {\n    public abstract /*a*/\n}\nabstract class Ab extends Base {\n    public abstract /*b*/\n}\nclass B extends Base {\n    public override [|m/*c*/|]\n}\nclass C extends Base {\n    override /*d*/\n}\nclass f extends Base {\n    protected /*f*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["method", "prop"] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "b", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "method", InsertText = "public abstract method(): void;", FilterText = "method", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }, new LSP.CompletionItem { Label = "prop", InsertText = "public abstract prop: number;", FilterText = "prop", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "c", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "method", TextEdit = U.InsertReplaceTextEdit("public override method(): void {\n}", f.Ranges()[0].LSRange), FilterText = "method", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }, new LSP.CompletionItem { Label = "prop", TextEdit = U.InsertReplaceTextEdit("public override prop: number;", f.Ranges()[0].LSRange), FilterText = "prop", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "d", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "method", InsertText = "override method(): void {\n}", FilterText = "method", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }, new LSP.CompletionItem { Label = "prop", InsertText = "protected override prop: number;", FilterText = "prop", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "f", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "prop", InsertText = "protected prop: number;", FilterText = "prop", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }], Excludes = ["method"] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
