// ported from: internal/fourslash/tests/quickInfoOnNewKeyword01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnNewKeyword01Case
{
    public static void TestQuickInfoOnNewKeyword01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Cat {\n  /**\n   * NOTE: this constructor is private! Please use the factory function\n   */\n  private constructor() { }\n\n  static makeCat() { new Cat(); }\n}\n\nne/*1*/w Ca/*2*/t();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "constructor Cat(): Cat", "NOTE: this constructor is private! Please use the factory function");
                f.VerifyQuickInfoAt(t, "2", "constructor Cat(): Cat", "NOTE: this constructor is private! Please use the factory function");
            }
            finally
            {
                done();
            }
        });
    }

}
