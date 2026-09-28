// ported from: internal/fourslash/tests/completionListOfGenericSymbol_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListOfGenericSymbolCase
{
    public static void TestCompletionListOfGenericSymbol(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var a = [1,2,3];\na./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "length", Detail = "(property) Array<number>.length: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Gets or sets the length of the array. This is a number one higher than the highest index in the array." } }, Kind = LSP.CompletionItemKind.Field }, new LSP.CompletionItem { Label = "toString", Detail = "(method) Array<number>.toString(): string", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Returns a string representation of an array." } }, Kind = LSP.CompletionItemKind.Method }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
