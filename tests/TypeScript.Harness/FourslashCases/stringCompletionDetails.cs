// ported from: internal/fourslash/tests/stringCompletionDetails_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StringCompletionDetailsCase
{
    public static void TestStringCompletionDetails(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const a: \"aa\" | \"bb\" = \"/**/\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "aa", Kind = LSP.CompletionItemKind.Constant, Detail = "aa", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { Range = new LSP.Range { Start = new LSP.Position { Line = 0, Character = 24 }, End = new LSP.Position { Line = 0, Character = 24 } }, NewText = "aa" } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
