// ported from: internal/fourslash/tests/codefixInferFromUsageNullish_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodefixInferFromUsageNullishCase
{
    public static void TestCodefixInferFromUsageNullish(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n// @noImplicitAny: true\ndeclare const a: string\nfunction wat([|b |]) {\n    b(a ?? 1);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "b: (arg0: string | number) => void", false, 0, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
