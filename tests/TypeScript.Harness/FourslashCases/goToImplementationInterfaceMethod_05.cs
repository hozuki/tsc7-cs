// ported from: internal/fourslash/tests/goToImplementationInterfaceMethod_05_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterfaceMethod_05Case
{
    public static void TestGoToImplementationInterfaceMethod_05(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    hello (): void;\n}\n\nclass SuperBar implements Foo {\n    [|hello|]() {}\n}\n\nclass Bar extends SuperBar {\n    hello2() {}\n}\n\nclass OtherBar extends SuperBar {\n    hello() {}\n    hello2() {}\n    hello3() {}\n}\n\nclass NotRelatedToBar {\n    hello() {}         // Equivalent to last case, but shares no common ancestors with Bar and so is not returned\n    hello2() {}\n    hello3() {}\n}\n\nclass NotBar extends SuperBar {\n    hello() {}         // Should not be returned because it is not structurally equivalent to Bar\n}\n\nfunction whatever(x: Bar) {\n    x.he/*function_call*/llo()\n}"u8);
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
