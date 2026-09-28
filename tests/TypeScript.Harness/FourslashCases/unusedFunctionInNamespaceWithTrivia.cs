// ported from: internal/fourslash/tests/unusedFunctionInNamespaceWithTrivia_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedFunctionInNamespaceWithTriviaCase
{
    public static void TestUnusedFunctionInNamespaceWithTrivia(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n[| namespace greeter {\n  // Do not remove\n  /**\n   * JSDoc Comment\n   */\n  function function1() {\n  }/*1*/\n} |]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "namespace greeter {\n    // Do not remove\n }", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
