// ported from: internal/fourslash/tests/pasteLambdaOverModule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PasteLambdaOverModuleCase
{
    public static void TestPasteLambdaOverModule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.Paste(t, "namespace B { }");
                f.GoToBOF(t);
                f.DeleteAtCaret(t, 15);
                f.Insert(t, "var t = (public x) => { };");
                f.VerifyNumberOfErrorsInCurrentFile(t, 1);
            }
            finally
            {
                done();
            }
        });
    }

}
