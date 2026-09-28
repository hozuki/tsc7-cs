// ported from: internal/fourslash/tests/findAllRefsFromContextualUnionType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsFromContextualUnionType1Case
{
    public static void TestFindAllRefsFromContextualUnionType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\nfunction test1(arg: { prop: \"foo\" }) {}\ntest1({ /*1*/prop: \"bar\" });\n\nfunction test2(arg: { prop: \"foo\" } | undefined) {}\ntest2({ /*2*/prop: \"bar\" });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
