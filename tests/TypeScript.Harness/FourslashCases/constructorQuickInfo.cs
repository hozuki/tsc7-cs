// ported from: internal/fourslash/tests/constructorQuickInfo_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ConstructorQuickInfoCase
{
    public static void TestConstructorQuickInfo(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class SS<T>{}\n\nvar x/*1*/1 = new SS<number>();\nvar x/*2*/2 = new SS();\nvar x/*3*/3 = new SS;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var x1: SS<number>", "");
                f.VerifyQuickInfoAt(t, "2", "var x2: SS<unknown>", "");
                f.VerifyQuickInfoAt(t, "3", "var x3: SS<unknown>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
