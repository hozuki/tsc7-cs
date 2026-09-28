// ported from: internal/fourslash/tests/letQuickInfoAndCompletionList_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class LetQuickInfoAndCompletionListCase
{
    public static void TestLetQuickInfoAndCompletionList(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let /*1*/a = 10;\n/*2*/a = 30;\nfunction foo() {\n    let /*3*/b = 20;\n    /*4*/b = /*5*/a;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", Detail = "let a: number" }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", Detail = "let a: number" }, new LSP.CompletionItem { Label = "b", Detail = "let b: number" }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", Detail = "let a: number" }, new LSP.CompletionItem { Label = "b", Detail = "let b: number" }] } });
                f.VerifyQuickInfoAt(t, "1", "let a: number", "");
                f.VerifyQuickInfoAt(t, "2", "let a: number", "");
                f.VerifyQuickInfoAt(t, "3", "let b: number", "");
                f.VerifyQuickInfoAt(t, "4", "let b: number", "");
                f.VerifyQuickInfoAt(t, "5", "let a: number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
