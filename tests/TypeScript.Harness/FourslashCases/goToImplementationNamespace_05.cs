// ported from: internal/fourslash/tests/goToImplementationNamespace_05_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationNamespace_05Case
{
    public static void TestGoToImplementationNamespace_05(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace /*implementation0*/Foo./*implementation2*/Baz {\n    export function hello() {}\n}\n\nmodule /*implementation1*/Bar./*implementation3*/Baz {\n    export function sure() {}\n}\n\nlet x = Fo/*reference0*/o;\nlet y = Ba/*reference1*/r;\nlet x1 = Foo.B/*reference2*/az;\nlet y1 = Bar.B/*reference3*/az;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "reference0", "reference1", "reference2", "reference3");
            }
            finally
            {
                done();
            }
        });
    }

}
