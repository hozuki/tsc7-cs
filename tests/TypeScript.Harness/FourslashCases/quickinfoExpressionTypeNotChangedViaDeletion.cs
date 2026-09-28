// ported from: internal/fourslash/tests/quickinfoExpressionTypeNotChangedViaDeletion_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoExpressionTypeNotChangedViaDeletionCase
{
    public static void TestQuickinfoExpressionTypeNotChangedViaDeletion(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("type TypeEq<A, B> = (<T>() => T extends A ? 1 : 2) extends (<T>() => T extends B ? 1 : 2) ? true : false;\n\nconst /*2*/test1: TypeEq<number[], [number, ...number[]]> = false;\n\ndeclare const foo: [number, ...number[]];\ndeclare const bar: number[];\n\nconst /*1*/test2: TypeEq<typeof foo, typeof bar> = false;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyQuickInfoIs(t, "const test2: false", "");
                f.GoToMarker(t, "2");
                f.VerifyQuickInfoIs(t, "const test1: false", "");
            }
            finally
            {
                done();
            }
        });
    }

}
