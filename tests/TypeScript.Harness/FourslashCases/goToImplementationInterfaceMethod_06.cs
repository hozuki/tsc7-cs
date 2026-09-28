// ported from: internal/fourslash/tests/goToImplementationInterfaceMethod_06_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterfaceMethod_06Case
{
    public static void TestGoToImplementationInterfaceMethod_06(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface SuperFoo {\n    hello (): void;\n}\n\ninterface Foo extends SuperFoo {\n    someOtherFunction(): void;\n}\n\nclass Bar implements Foo {\n     [|hello|]() {}\n     someOtherFunction() {}\n}\n\nfunction createFoo(): Foo {\n    return {\n        [|hello|]() {},\n        someOtherFunction() {}\n    };\n}\n\nvar y: Foo = {\n    [|hello|]() {},\n    someOtherFunction() {}\n};\n\nclass FooLike implements SuperFoo {\n     hello() {}\n     someOtherFunction() {}\n}\n\nclass NotRelatedToFoo {\n     hello() {}                // This case is equivalent to the last case, but is not returned because it does not share a common ancestor with Foo\n     someOtherFunction() {}\n}\n\nclass NotFoo implements SuperFoo {\n     hello() {}                // We only want implementations of Foo, even though the function is declared in SuperFoo\n}\n\nfunction (x: Foo) {\n    x.he/*function_call*/llo()\n}"u8);
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
