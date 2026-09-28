// ported from: internal/fourslash/tests/goToImplementationInterfaceMethod_01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToImplementationInterfaceMethod_01Case
{
    public static void TestGoToImplementationInterfaceMethod_01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    hel/*declaration*/lo(): void;\n    okay?: number;\n}\n\nclass Bar implements Foo {\n    [|hello|]() {}\n    public sure() {}\n}\n\nfunction whatever(a: Foo) {\n    a.he/*function_call*/llo();\n}\n\nwhatever(new Bar());"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToImplementation(t, "function_call", "declaration");
            }
            finally
            {
                done();
            }
        });
    }

}
