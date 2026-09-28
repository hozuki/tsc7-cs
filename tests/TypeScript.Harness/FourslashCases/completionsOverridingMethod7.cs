// ported from: internal/fourslash/tests/completionsOverridingMethod7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethod7Case
{
    public static void TestCompletionsOverridingMethod7(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n// @newline: LF\nabstract class Base {\n    abstract M<T>(t: T): void;\n    abstract M<T>(t: T, x: number): void;\n}\n\nabstract class Derived extends Base {\n    abstract /*a*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "M", InsertText = "abstract M<T>(t: T): void;\nabstract M<T>(t: T, x: number): void;", FilterText = "M", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyApplyCodeActionFromCompletion(t, "a", new F.ApplyCodeActionFromCompletionOptions { UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue }, Name = "M", Source = "ClassMemberSnippet/", Description = "Update modifiers of 'M'", NewFileContent = "abstract class Base {\n    abstract M<T>(t: T): void;\n    abstract M<T>(t: T, x: number): void;\n}\n\nabstract class Derived extends Base {\n    \n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
