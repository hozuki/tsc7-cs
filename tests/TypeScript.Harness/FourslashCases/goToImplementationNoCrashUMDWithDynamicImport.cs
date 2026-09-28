// ported from: internal/fourslash/tests/goToImplementationNoCrashUMDWithDynamicImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationNoCrashUMDWithDynamicImportCase
{
    public static void TestGoToImplementationNoCrashUMDWithDynamicImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /lib.d.ts\nexport as namespace Lib;\nexport interface /*1*/IFoo {}\n// @Filename: /user.ts\nconst p = import('./lib');"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
