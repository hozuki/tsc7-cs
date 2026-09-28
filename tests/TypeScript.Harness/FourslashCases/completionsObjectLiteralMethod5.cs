// ported from: internal/fourslash/tests/completionsObjectLiteralMethod5_test.go
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

internal static class CompletionsObjectLiteralMethod5Case
{
    public static void TestCompletionsObjectLiteralMethod5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: a.ts\ninterface Foo {\n    method(x?: string): void;\n}\nconst foo: Foo = {\n    /*m*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true, LabelDetailsSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "m", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "method", SortText = LS.ObjectLiteralPropertySortText(LS.SortTextLocationPriority, "method") }, new LSP.CompletionItem { Label = "method", InsertText = "method(x) {\n    $0\n},", SortText = LS.SortBelow(LS.ObjectLiteralPropertySortText(LS.SortTextLocationPriority, "method")), Data = new LSP.CompletionItemData { Source = "ObjectLiteralMethodSnippet/" }, InsertTextFormat = LSP.InsertTextFormat.Snippet, LabelDetails = new LSP.CompletionItemLabelDetails { Detail = "(x)" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithObjectLiteralMethodSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
