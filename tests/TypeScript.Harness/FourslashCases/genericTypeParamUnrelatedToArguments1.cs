// ported from: internal/fourslash/tests/genericTypeParamUnrelatedToArguments1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericTypeParamUnrelatedToArguments1Case
{
    public static void TestGenericTypeParamUnrelatedToArguments1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo<T> {\n    new (x: number): Foo<T>;\n}\ndeclare var f/*1*/1: Foo<number>;\nvar f/*2*/2: Foo<number>;\nvar f/*3*/3 = new Foo(3);\nvar f/*4*/4: Foo<number> = new Foo(3);\nvar f/*5*/5 = new Foo<number>(3);\nvar f/*6*/6: Foo<number> = new Foo<number>(3);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var f1: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "2", "var f2: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "3", "var f3: any", "");
                f.VerifyQuickInfoAt(t, "4", "var f4: Foo<number>", "");
                f.VerifyQuickInfoAt(t, "5", "var f5: any", "");
                f.VerifyQuickInfoAt(t, "6", "var f6: Foo<number>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
