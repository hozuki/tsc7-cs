// ported from: internal/fourslash/tests/goToTypeWithTupleTypes_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToTypeWithTupleTypesCase
{
    public static void TestGoToTypeWithTupleTypes1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nexport let x/*1*/: [number, number] = [1, 2];\n\ntype DoubleTupleTrouble<T> = [T, T];\n\nexport let y/*2*/: DoubleTupleTrouble<number> = [1, 2];\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToTypeDefinition(t, f.MarkerNames());
            }
            finally
            {
                done();
            }
        });
    }

}
