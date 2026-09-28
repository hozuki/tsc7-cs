// ported from: internal/fourslash/tests/memberListInReopenedEnum_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MemberListInReopenedEnumCase
{
    public static void TestMemberListInReopenedEnum(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace M {\n    enum E {\n        A, B\n    }\n    enum E {\n        C = 0, D\n    }\n    var x = E./*1*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "A", Detail = "(enum member) E.A = 0" }, new LSP.CompletionItem { Label = "B", Detail = "(enum member) E.B = 1" }, new LSP.CompletionItem { Label = "C", Detail = "(enum member) E.C = 0" }, new LSP.CompletionItem { Label = "D", Detail = "(enum member) E.D = 1" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
