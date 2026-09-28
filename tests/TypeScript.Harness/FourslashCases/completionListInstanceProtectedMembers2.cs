// ported from: internal/fourslash/tests/completionListInstanceProtectedMembers2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInstanceProtectedMembers2Case
{
    public static void TestCompletionListInstanceProtectedMembers2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base {\n    private privateMethod() { }\n    private privateProperty;\n\n    protected protectedMethod() { }\n    protected protectedProperty;\n\n    public publicMethod() { }\n    public publicProperty;\n\n    protected protectedOverriddenMethod() { }\n    protected protectedOverriddenProperty;\n}\n\nclass C1 extends Base {\n    protected  protectedOverriddenMethod() { }\n    protected  protectedOverriddenProperty;\n\n    test() {\n        this./*1*/;\n        super./*2*/;\n\n        var b: Base;\n        var c: C1;\n\n        b./*3*/;\n        c./*4*/;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["protectedMethod", "protectedProperty", "publicMethod", "publicProperty", "protectedOverriddenMethod", "protectedOverriddenProperty"], Excludes = ["privateMethod", "privateProperty"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["protectedMethod", "publicMethod", "protectedOverriddenMethod"], Excludes = ["privateMethod", "privateProperty", "protectedProperty", "publicProperty", "protectedOverriddenProperty"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["publicMethod", "publicProperty"], Excludes = ["privateMethod", "privateProperty", "protectedMethod", "protectedProperty", "protectedOverriddenMethod", "protectedOverriddenProperty"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["protectedMethod", "protectedProperty", "publicMethod", "publicProperty", "protectedOverriddenMethod", "protectedOverriddenProperty"], Excludes = ["privateMethod", "privateProperty"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
