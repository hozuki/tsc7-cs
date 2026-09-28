// ported from: internal/fourslash/tests/goToImplementationClassMethod_01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationClassMethod_01Case
{
    public static void TestGoToImplementationClassMethod_01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("abstract class AbstractBar {\n    abstract he/*declaration*/llo(): void;\n}\n\nclass Bar extends AbstractBar{\n    [|hello|]() {}\n}\n\nfunction whatever(x: AbstractBar) {\n    x.he/*reference*/llo();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "reference", "declaration");
            }
            finally
            {
                done();
            }
        });
    }

}
