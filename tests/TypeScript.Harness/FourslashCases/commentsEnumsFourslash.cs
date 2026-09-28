// ported from: internal/fourslash/tests/commentsEnumsFourslash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CommentsEnumsFourslashCase
{
    public static void TestCommentsEnumsFourslash(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** Enum of colors*/\nenum /*1*/Colors {\n    /** Fancy name for 'blue'*/\n    /*2*/Cornflower,\n    /** Fancy name for 'pink'*/\n    /*3*/FancyPink\n}\nvar /*4*/x = /*5*/Colors./*6*/Cornflower;\nx = Colors./*7*/FancyPink;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "enum Colors", "Enum of colors");
                f.VerifyQuickInfoAt(t, "2", "(enum member) Colors.Cornflower = 0", "Fancy name for 'blue'");
                f.VerifyQuickInfoAt(t, "3", "(enum member) Colors.FancyPink = 1", "Fancy name for 'pink'");
                f.VerifyQuickInfoAt(t, "4", "var x: Colors", "");
                f.VerifyQuickInfoAt(t, "5", "enum Colors", "Enum of colors");
                f.VerifyQuickInfoAt(t, "6", "(enum member) Colors.Cornflower = 0", "Fancy name for 'blue'");
                f.VerifyQuickInfoAt(t, "7", "(enum member) Colors.FancyPink = 1", "Fancy name for 'pink'");
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "Colors", Detail = "enum Colors", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Enum of colors" } } }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["6", "7"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "Cornflower", Detail = "(enum member) Colors.Cornflower = 0", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Fancy name for 'blue'" } } }, new LSP.CompletionItem { Label = "FancyPink", Detail = "(enum member) Colors.FancyPink = 1", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Fancy name for 'pink'" } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
