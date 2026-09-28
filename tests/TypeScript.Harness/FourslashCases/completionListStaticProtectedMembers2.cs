// ported from: internal/fourslash/tests/completionListStaticProtectedMembers2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListStaticProtectedMembers2Case
{
    public static void TestCompletionListStaticProtectedMembers2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: es2015\n// @lib: es5\nclass Base {\n    private static privateMethod() { }\n    private static privateProperty;\n\n    protected static protectedMethod() { }\n    protected static protectedProperty;\n\n    public static publicMethod() { }\n    public static publicProperty;\n\n    protected static protectedOverriddenMethod() { }\n    protected static protectedOverriddenProperty;\n}\n\nclass C2 extends Base {\n    protected static protectedOverriddenMethod() { }\n    protected static protectedOverriddenProperty;\n\n    static test() {\n        Base./*1*/;\n        C2./*2*/;\n        this./*3*/;\n        super./*4*/;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "protectedMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedOverriddenMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedOverriddenProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }]) } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "protectedMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedOverriddenMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedOverriddenProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "test", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }]) } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "protectedMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedOverriddenMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedOverriddenProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "protectedProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "apply", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "arguments", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "bind", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "call", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "caller", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "length", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "toString", SortText = LS.SortTextLocationPriority }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
