// ported from: internal/fourslash/tests/completionsOverridingMethod18_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethod18Case
{
    public static void TestCompletionsOverridingMethod18(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n// @newline: LF\ndeclare function decorator(...args: any[]): any;\nclass DecoratorBase {\n    protected foo(a: string): string;\n    protected foo(a: number): number;\n    protected foo(a: any): any {\n        return a;\n    }\n}\nclass DecoratorSub extends DecoratorBase {\n    @decorator protected /**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", InsertText = "protected foo(a: string): string;\nprotected foo(a: number): number;\n@decorator\nprotected foo(a: any) {\n}", FilterText = "foo", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue }, Name = "foo", Source = "ClassMemberSnippet/", Description = "Update modifiers of 'foo'", NewFileContent = "declare function decorator(...args: any[]): any;\nclass DecoratorBase {\n    protected foo(a: string): string;\n    protected foo(a: number): number;\n    protected foo(a: any): any {\n        return a;\n    }\n}\nclass DecoratorSub extends DecoratorBase {\n    \n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
