// ported from: internal/fourslash/tests/quickInfoOnGenericWithConstraints1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoOnGenericWithConstraints1Case
{
    public static void TestQuickInfoOnGenericWithConstraints1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Fo/*1*/o<T/*2*/T extends Date> {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "interface Foo<TT extends Date>", "");
                f.VerifyQuickInfoAt(t, "2", "(type parameter) TT in Foo<TT extends Date>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
