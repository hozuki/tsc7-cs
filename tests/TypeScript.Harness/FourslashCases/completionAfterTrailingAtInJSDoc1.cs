// ported from: internal/fourslash/tests/completionAfterTrailingAtInJSDoc1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionAfterTrailingAtInJSDoc1Case
{
    public static void TestCompletionAfterTrailingAtInJSDoc1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /atTagPosition.js\n/**\n * @/*1*/\n */\nfunction foo(x) {}\n\n// @Filename: /atAfterExistingParam.js\n/**\n * @param {string} x ok\n * @/*2*/\n */\nfunction bar(x, y) {}\n\n// @Filename: /atMidLine.js\n/**\n * some text @/*3*/\n */\nfunction baz(y) {}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2", "3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "param", Kind = LSP.CompletionItemKind.Keyword }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
