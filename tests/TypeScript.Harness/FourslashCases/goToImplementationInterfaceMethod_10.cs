// ported from: internal/fourslash/tests/goToImplementationInterfaceMethod_10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterfaceMethod_10Case
{
    public static void TestGoToImplementationInterfaceMethod_10(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface BaseFoo {\n\t hello(): void;\n}\n\ninterface Foo extends BaseFoo {\n\t aloha(): void;\n}\n\ninterface Bar {\n \t hello(): void;\n \t goodbye(): void;\n}\n\nclass FooImpl implements Foo {\n \t [|hello|]() {/**FooImpl*/}\n \t aloha() {}\n}\n\nclass BaseFooImpl implements BaseFoo {\n \t hello() {/**BaseFooImpl*/}    // Should not show up\n}\n\nclass BarImpl implements Bar {\n\t [|hello|]() {/**BarImpl*/}\n\t goodbye() {}\n}\n\nclass FooAndBarImpl implements Foo, Bar {\n\t [|hello|]() {/**FooAndBarImpl*/}\n\t aloha() {}\n\t goodbye() {}\n}\n\nfunction someFunction(x: Foo | Bar) {\n\t x.he/*function_call0*/llo();\n}\n\nfunction anotherFunction(x: Foo & Bar) {\n\t x.he/*function_call1*/llo();\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "function_call0", "function_call1");
            }
            finally
            {
                done();
            }
        });
    }

}
