// ported from: internal/fourslash/tests/quickInfoGenericPropertyAccessor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoGenericPropertyAccessorCase
{
    public static void TestQuickInfoGenericPropertyAccessor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ndeclare const o: {\n    f: <T>(x: T) => T\n    get g(): <T>(x: T) => T\n}\n\ndeclare const x: number\n\no.f/*1*/(x)\no.g/*2*/(x)\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) f: <number>(x: number) => number", "");
                f.VerifyQuickInfoAt(t, "2", "(accessor) g: <number>(x: number) => number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
