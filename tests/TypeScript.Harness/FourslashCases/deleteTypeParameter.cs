// ported from: internal/fourslash/tests/deleteTypeParameter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DeleteTypeParameterCase
{
    public static void TestDeleteTypeParameter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Query<T> {\n    groupBy(): Query</**/T>;\n}\ninterface Query2<T> {\n    groupBy(): Query2<Query<T>>;\n}\nvar q1: Query<number>;\nvar q2: Query2<number>;\nq1 = q2;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.DeleteAtCaret(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
