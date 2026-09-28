// ported from: internal/fourslash/tests/completionInJSDocPropertyWithLinkNoCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionInJSDocPropertyWithLinkNoCrash1Case
{
    public static void TestCompletionInJSDocPropertyWithLinkNoCrash1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @allowJs: true\n// @filename: /file.js\nexport function foo() {}\n\n/**\n * @typedef MyType\n * @property {number} [timeout] - The /*1*/timeout; defaults to {@linkcode DEFAULT}\n */\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [".", ",", ";"] }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done();
            }
        });
    }

}
