// ported from: internal/fourslash/tests/noErrorsAfterCompletionsRequestWithinGenericFunction1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NoErrorsAfterCompletionsRequestWithinGenericFunction1Case
{
    public static void TestNoErrorsAfterCompletionsRequestWithinGenericFunction1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n\ndeclare function func<T extends { foo: 1 }>(arg: T): void;\nfunc({ foo: 1, bar/*1*/: 1 });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyCompletions(t, null!, null!);
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
