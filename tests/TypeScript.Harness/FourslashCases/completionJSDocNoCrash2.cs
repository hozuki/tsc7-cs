// ported from: internal/fourslash/tests/completionJSDocNoCrash2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionJSDocNoCrash2Case
{
    public static void TestCompletionJSDocNoCrash2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @allowJs: true\n// @filename: file.js\n/**\n * @param {Object} obj\n * @param {string} obj.first The first property\n * @param {string} obj.second The second property\n * @param {string} obj.third The {@link foo} third property\n */\n/*1*/function foo(obj) {}\n"u8);
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
