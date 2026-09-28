// ported from: internal/fourslash/tests/completionsWithDeprecatedTag5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsWithDeprecatedTag5Case
{
    public static void TestCompletionsWithDeprecatedTag5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nclass Foo {\n    /** @deprecated m */\n    static m() {}\n}\nFoo./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "m", Kind = LSP.CompletionItemKind.Method, SortText = LS.DeprecateSortText(LS.SortTextLocalDeclarationPriority), Tags = [LSP.CompletionItemTag.Deprecated] }]) } });
            }
            finally
            {
                done();
            }
        });
    }

}
