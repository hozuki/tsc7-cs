// ported from: internal/fourslash/tests/completionListStaticProtectedMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListStaticProtectedMembersCase
{
    public static void TestCompletionListStaticProtectedMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Base {\n    private static privateMethod() { }\n    private static privateProperty;\n\n    protected static protectedMethod() { }\n    protected static protectedProperty;\n\n    public static publicMethod() { }\n    public static publicProperty;\n\n    protected static protectedOverriddenMethod() { }\n    protected static protectedOverriddenProperty;\n\n    static test() {\n        Base./*1*/;\n        this./*2*/;\n        C1./*3*/;\n    }\n}\n\nclass C1 extends Base {\n    protected static protectedOverriddenMethod() { }\n    protected static protectedOverriddenProperty;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "privateMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "privateProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedOverriddenMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedOverriddenProperty", SortText = LS.SortTextLocalDeclarationPriority }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "privateMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "privateProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicProperty", SortText = LS.SortTextLocalDeclarationPriority }], Excludes = ["protectedOverriddenMethod", "protectedOverriddenProperty"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
