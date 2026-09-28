// ported from: internal/fourslash/tests/contextuallyTypedFunctionExpressionGeneric1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ContextuallyTypedFunctionExpressionGeneric1Case
{
    public static void TestContextuallyTypedFunctionExpressionGeneric1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Comparable<T> {\n   compareTo(other: T): T;\n}\ninterface Comparer {\n   <T extends Comparable<T>>(x: T, y: T): T;\n}\nvar max2: Comparer = (x/*1*/x, y/*2*/y) => { return x/*3*/x.compareTo(y/*4*/y) };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) xx: T extends Comparable<T>", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) yy: T extends Comparable<T>", "");
                f.VerifyQuickInfoAt(t, "3", "(parameter) xx: T extends Comparable<T>", "");
                f.VerifyQuickInfoAt(t, "4", "(parameter) yy: T extends Comparable<T>", "");
            }
            finally
            {
                done();
            }
        });
    }

}
