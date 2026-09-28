// ported from: internal/fourslash/tests/goToImplementationInterface_08_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterface_08Case
{
    public static void TestGoToImplementationInterface_08(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Base {\n    hello (): void;\n}\n\ninterface A extends Base {}\ninterface B extends C, A {}\ninterface C extends B, A {}\n\nclass X implements B {\n    [|hello|]() {}\n}\n\nfunction someFunction(d : A) {\n    d.he/*function_call*/llo();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "function_call");
            }
            finally
            {
                done();
            }
        });
    }

}
