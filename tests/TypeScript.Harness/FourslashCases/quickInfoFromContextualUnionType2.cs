// ported from: internal/fourslash/tests/quickInfoFromContextualUnionType2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoFromContextualUnionType2Case
{
    public static void TestQuickInfoFromContextualUnionType2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\nfunction test1(arg: { prop: \"foo\" }) {}\ntest1({ /*1*/prop: \"bar\" });\n\nfunction test2(arg: { prop: \"foo\" } | undefined) {}\ntest2({ /*2*/prop: \"bar\" });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) prop: \"foo\"", "");
                f.VerifyQuickInfoAt(t, "2", "(property) prop: \"foo\"", "");
            }
            finally
            {
                done();
            }
        });
    }

}
