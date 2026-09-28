// ported from: internal/fourslash/tests/goToImplementationInterfaceMethod_03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterfaceMethod_03Case
{
    public static void TestGoToImplementationInterfaceMethod_03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    hello (): void;\n}\n\nclass Bar extends SuperBar {\n    [|hello|]() {}\n}\n\nclass SuperBar implements Foo {\n    hello() {} // should not show up\n}\n\nclass OtherBar implements Foo {\n    hello() {} // should not show up\n}\n\nnew Bar().hel/*function_call*/lo();\nnew Bar()[\"hello\"]();"u8);
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
