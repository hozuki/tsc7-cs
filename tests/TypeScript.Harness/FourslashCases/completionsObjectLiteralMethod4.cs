// ported from: internal/fourslash/tests/completionsObjectLiteralMethod4_test.go
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

internal static class CompletionsObjectLiteralMethod4Case
{
    public static void TestCompletionsObjectLiteralMethod4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: a.ts\ninterface IFoo {\n    bar(this: IFoo): void;\n}\nconst obj: IFoo = {\n    /*1*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true, LabelDetailsSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "bar", SortText = LS.ObjectLiteralPropertySortText(LS.SortTextLocationPriority, "bar") }, new LSP.CompletionItem { Label = "bar", InsertText = "bar() {\n    $0\n},", SortText = LS.SortBelow(LS.ObjectLiteralPropertySortText(LS.SortTextLocationPriority, "bar")), Data = new LSP.CompletionItemData { Source = "ObjectLiteralMethodSnippet/" }, InsertTextFormat = LSP.InsertTextFormat.Snippet, LabelDetails = new LSP.CompletionItemLabelDetails { Detail = "()" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithObjectLiteralMethodSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
