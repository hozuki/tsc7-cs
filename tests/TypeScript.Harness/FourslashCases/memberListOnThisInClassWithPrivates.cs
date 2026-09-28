// ported from: internal/fourslash/tests/memberListOnThisInClassWithPrivates_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MemberListOnThisInClassWithPrivatesCase
{
    public static void TestMemberListOnThisInClassWithPrivates(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C1 {\n   public pubMeth() {this./**/} // test on 'this.'\n   private privMeth() {}\n   public pubProp = 0;\n   private privProp = 0;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "privMeth", Detail = "(method) C1.privMeth(): void" }, new LSP.CompletionItem { Label = "privProp", Detail = "(property) C1.privProp: number" }, new LSP.CompletionItem { Label = "pubMeth", Detail = "(method) C1.pubMeth(): void" }, new LSP.CompletionItem { Label = "pubProp", Detail = "(property) C1.pubProp: number" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
