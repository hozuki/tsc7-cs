// ported from: internal/fourslash/tests/genericSignaturesAreProperlyCleaned_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericSignaturesAreProperlyCleanedCase
{
    public static void TestGenericSignaturesAreProperlyCleaned(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Int<T> {\nval<U>(f: (t: T) => U): Int<U>;\n}\ndeclare var v1: Int<string>;\nvar v2: Int<number> = v1/*1*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
                f.GoToMarker(t, "1");
                f.DeleteAtCaret(t, 1);
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
