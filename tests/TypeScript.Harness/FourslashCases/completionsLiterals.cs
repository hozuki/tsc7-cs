// ported from: internal/fourslash/tests/completionsLiterals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsLiteralsCase
{
    public static void TestCompletionsLiterals(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const x: 0 | \"one\" = /**/;\nconst y: 0 | \"one\" | 1n = /*1*/;\nconst y2: 0 | \"one\" | 1n = 'one'/*2*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "0", Kind = LSP.CompletionItemKind.Constant, Detail = "0" }, new LSP.CompletionItem { Label = "\"one\"", Kind = LSP.CompletionItemKind.Constant, Detail = "\"one\"" }] } });
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "0", Kind = LSP.CompletionItemKind.Constant, Detail = "0" }, new LSP.CompletionItem { Label = "\"one\"", Kind = LSP.CompletionItemKind.Constant, Detail = "\"one\"" }, new LSP.CompletionItem { Label = "1n", Kind = LSP.CompletionItemKind.Constant, Detail = "1n" }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["\"one\""] } });
            }
            finally
            {
                done();
            }
        });
    }

}
