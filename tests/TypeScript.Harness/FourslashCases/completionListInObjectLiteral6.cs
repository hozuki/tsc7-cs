// ported from: internal/fourslash/tests/completionListInObjectLiteral6_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInObjectLiteral6Case
{
    public static void TestCompletionListInObjectLiteral6(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const foo = {\n    a: \"a\",\n    b: \"b\"\n};\nfunction fn<T extends { [key: string]: any }>(obj: T, events: { [Key in `on_${string & keyof T}`]?: Key }) {}\n\nfn(foo, {\n    /*1*/\n})\nfn({ a: \"a\", b: \"b\" }, {\n    /*2*/\n})"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "on_a?", InsertText = "on_a", FilterText = "on_a", SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "on_b?", InsertText = "on_b", FilterText = "on_b", SortText = LS.SortTextOptionalMember }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
