// ported from: internal/fourslash/tests/completionListForObjectSpread_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListForObjectSpreadCase
{
    public static void TestCompletionListForObjectSpread(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let o = { a: 1, b: 'no' }\nlet o2 = { b: 'yes', c: true }\nlet swap = { a: 'yes', b: -1 };\nlet addAfter: { a: number, b: string, c: boolean } =\n    { ...o, c: false }\nlet addBefore: { a: number, b: string, c: boolean } =\n    { c: false, ...o }\nlet ignore: { a: number, b: string } =\n    { b: 'ignored', ...o }\nignore./*1*/a;\nlet combinedNestedChangeType: { a: number, b: boolean, c: number } =\n    { ...{ a: 1, ...{ b: false, c: 'overriden' } }, c: -1 }\ncombinedNestedChangeType./*2*/a;\nlet spreadNull: { a: number } =\n    { a: 7, ...null }\nlet spreadUndefined: { a: number } =\n    { a: 7, ...undefined }\nspreadNull./*3*/a;\nspreadUndefined./*4*/a;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a", Detail = "(property) a: number" }, new LSP.CompletionItem { Label = "b", Detail = "(property) b: string" }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a", Detail = "(property) a: number" }, new LSP.CompletionItem { Label = "b", Detail = "(property) b: boolean" }, new LSP.CompletionItem { Label = "c", Detail = "(property) c: number" }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a", Detail = "(property) a: number" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
