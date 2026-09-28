// ported from: internal/fourslash/tests/unusedClassInNamespaceWithTrivia2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class UnusedClassInNamespaceWithTrivia2Case
{
    public static void TestUnusedClassInNamespaceWithTrivia2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n[| namespace greeter {\n  // Do not remove\n  /**\n   * JSDoc Comment\n   */\n  class /* comment2 */ class1 {\n  }\n} |]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "namespace greeter {\n   // Do not remove\n}", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
