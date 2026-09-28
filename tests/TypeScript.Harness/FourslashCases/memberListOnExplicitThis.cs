// ported from: internal/fourslash/tests/memberListOnExplicitThis_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MemberListOnExplicitThisCase
{
    public static void TestMemberListOnExplicitThis(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Restricted {\n   n: number;\n}\nclass C1 implements Restricted {\n   n: number;\n   m: number;\n   f(this: this) {this./*1*/} // test on 'this.'\n   g(this: Restricted) {this./*2*/}\n}\nfunction f(this: void) {this./*3*/}\nfunction g(this: Restricted) {this./*4*/}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "f", Detail = "(method) C1.f(this: this): void" }, new LSP.CompletionItem { Label = "g", Detail = "(method) C1.g(this: Restricted): void" }, new LSP.CompletionItem { Label = "m", Detail = "(property) C1.m: number" }, new LSP.CompletionItem { Label = "n", Detail = "(property) C1.n: number" }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "n", Detail = "(property) Restricted.n: number" }] } });
                f.VerifyCompletions(t, "3", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
