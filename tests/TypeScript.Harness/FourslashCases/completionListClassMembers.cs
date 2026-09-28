// ported from: internal/fourslash/tests/completionListClassMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListClassMembersCase
{
    public static void TestCompletionListClassMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nclass Class {\n    private privateInstanceMethod() { }\n    public publicInstanceMethod() { }\n\n    private privateProperty = 1;\n    public publicProperty = 1;\n\n    private static privateStaticProperty = 1;\n    public static publicStaticProperty = 1;\n\n    private static privateStaticMethod() { }\n    public static publicStaticMethod() {\n        Class./*staticsInsideClassScope*/publicStaticMethod();\n        var c = new Class();\n        c./*instanceMembersInsideClassScope*/privateProperty;\n    }\n}\n\nClass./*staticsOutsideClassScope*/publicStaticMethod();\nvar c = new Class();\nc./*instanceMembersOutsideClassScope*/privateProperty;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "staticsInsideClassScope", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "privateStaticMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "privateStaticProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicStaticMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicStaticProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }]) } });
                f.VerifyCompletions(t, "instanceMembersInsideClassScope", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["privateInstanceMethod", "publicInstanceMethod", "privateProperty", "publicProperty"] } });
                f.VerifyCompletions(t, "staticsOutsideClassScope", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "publicStaticMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "publicStaticProperty", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }]) } });
                f.VerifyCompletions(t, "instanceMembersOutsideClassScope", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["publicInstanceMethod", "publicProperty"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
