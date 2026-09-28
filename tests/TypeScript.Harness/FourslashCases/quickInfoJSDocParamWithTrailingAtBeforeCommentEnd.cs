// ported from: internal/fourslash/tests/quickInfoJSDocParamWithTrailingAtBeforeCommentEnd_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSDocParamWithTrailingAtBeforeCommentEndCase
{
    public static void TestQuickInfoJSDocParamWithTrailingAtBeforeCommentEnd(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\n/** @param {string} x trailing @/*at*/*/\nfunction /*fn*/foo(/*x*/x) {}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "fn", "function foo(x: string): void", "\n\n*@param* `x` \u2014 trailing @");
                f.VerifyQuickInfoAt(t, "x", "(parameter) x: string", "trailing @");
                f.VerifyCompletions(t, "at", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "param", Kind = LSP.CompletionItemKind.Keyword }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
