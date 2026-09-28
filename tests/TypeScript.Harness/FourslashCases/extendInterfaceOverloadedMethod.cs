// ported from: internal/fourslash/tests/extendInterfaceOverloadedMethod_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ExtendInterfaceOverloadedMethodCase
{
    public static void TestExtendInterfaceOverloadedMethod(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface A<T> {\n    foo(a: T): B<T>;\n    foo(): void ;\n    foo2(): B<number>;\n}\ninterface B<T> extends A<T> {\n    bar(): void ;\n}\nvar b: B<number>;\nvar /**/x = b.foo2().foo(5).foo(); // 'x' is of type 'void'"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "var x: void", "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
