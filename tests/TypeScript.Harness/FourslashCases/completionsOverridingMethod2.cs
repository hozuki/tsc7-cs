// ported from: internal/fourslash/tests/completionsOverridingMethod2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethod2Case
{
    public static void TestCompletionsOverridingMethod2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: a.ts\ninterface DollarSign {\n    \"$usd\"(a: number): number;\n    $cad(b: number): number;\n    cla$$y(c: number): number;\n    isDollarAmountString(s: string): s is `$${number}`\n}\nclass USD implements DollarSign {\n    /*a*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "$usd", InsertText = "\"\\$usd\"(a: number): number {\n    $0\n}", FilterText = "$usd", SortText = LS.SortTextLocationPriority, InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "$cad", InsertText = "\\$cad(b: number): number {\n    $0\n}", FilterText = "$cad", SortText = LS.SortTextLocationPriority, InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "cla$$y", InsertText = "cla\\$\\$y(c: number): number {\n    $0\n}", FilterText = "cla$$y", SortText = LS.SortTextLocationPriority, InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "isDollarAmountString", InsertText = "isDollarAmountString(s: string): s is `\\$\\${number}` {\n    $0\n}", FilterText = "isDollarAmountString", SortText = LS.SortTextLocationPriority, InsertTextFormat = LSP.InsertTextFormat.Snippet }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
