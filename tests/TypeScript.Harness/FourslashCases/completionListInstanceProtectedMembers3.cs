// ported from: internal/fourslash/tests/completionListInstanceProtectedMembers3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInstanceProtectedMembers3Case
{
    public static void TestCompletionListInstanceProtectedMembers3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base {\n    private privateMethod() { }\n    private privateProperty;\n\n    protected protectedMethod() { }\n    protected protectedProperty;\n\n    public publicMethod() { }\n    public publicProperty;\n\n    protected protectedOverriddenMethod() { }\n    protected protectedOverriddenProperty;\n}\n\nclass C1 extends Base {\n    protected  protectedOverriddenMethod() { }\n    protected  protectedOverriddenProperty;\n}\n\n var b: Base;\n var c: C1;\n b./*1*/;\n c./*2*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["publicMethod", "publicProperty"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
