// ported from: internal/fourslash/tests/completionListInClassStaticBlocks_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInClassStaticBlocksCase
{
    public static void TestCompletionListInClassStaticBlocks(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @target: esnext\nclass Foo {\n    static #a = 1;\n    static a() {\n        this./*1*/\n    }\n    static b() {\n        Foo./*2*/\n    }\n    static {\n        this./*3*/\n    }\n    static {\n        Foo./*4*/\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2", "3", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "#a", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "a", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "b", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "prototype" }]) } });
            }
            finally
            {
                done();
            }
        });
    }

}
