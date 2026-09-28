// ported from: internal/fourslash/tests/quickInfoForDerivedGenericTypeWithConstructor_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForDerivedGenericTypeWithConstructorCase
{
    public static void TestQuickInfoForDerivedGenericTypeWithConstructor(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A<T> {\n    foo() { }\n}\nclass B<T> extends A<T> {\n    bar() { }\n    constructor() { super() }\n}\nclass B2<T> extends A<T> {\n    bar() { }\n}\nvar /*1*/b: B<number>;\nvar /*2*/b2: B<number>;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var b: B<number>", "");
                f.VerifyQuickInfoAt(t, "2", "var b2: B<number>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
