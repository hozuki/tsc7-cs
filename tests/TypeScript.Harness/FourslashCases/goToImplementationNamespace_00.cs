// ported from: internal/fourslash/tests/goToImplementationNamespace_00_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationNamespace_00Case
{
    public static void TestGoToImplementationNamespace_00(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace /*implementation0*/Foo {\n    export function hello() {}\n}\n\nmodule /*implementation1*/Bar {\n    export function sure() {}\n}\n\nlet x = Fo/*reference0*/o;\nlet y = Ba/*reference1*/r;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "reference0", "reference1");
            }
            finally
            {
                done();
            }
        });
    }

}
