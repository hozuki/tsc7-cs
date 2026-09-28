// ported from: internal/fourslash/tests/emptyArrayInference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class EmptyArrayInferenceCase
{
    public static void TestEmptyArrayInference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nvar x/*1*/x = true ? [1] : [undefined]; \nvar y/*2*/y = true ? [1] : [];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var xx: number[]", "");
                f.VerifyQuickInfoAt(t, "2", "var yy: number[]", "");
            }
            finally
            {
                done();
            }
        });
    }

}
