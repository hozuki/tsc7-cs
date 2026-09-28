// ported from: internal/fourslash/tests/restParamsContextuallyTyped_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RestParamsContextuallyTypedCase
{
    public static void TestRestParamsContextuallyTyped(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var foo: Function = function (/*1*/a, /*2*/b, /*3*/c) { };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) a: any", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) b: any", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) c: any", "");
            }
            finally
            {
                done();
            }
        });
    }

}
