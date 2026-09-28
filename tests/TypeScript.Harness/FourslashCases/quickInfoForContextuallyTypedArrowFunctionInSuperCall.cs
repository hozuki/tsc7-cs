// ported from: internal/fourslash/tests/quickInfoForContextuallyTypedArrowFunctionInSuperCall_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForContextuallyTypedArrowFunctionInSuperCallCase
{
    public static void TestQuickInfoForContextuallyTypedArrowFunctionInSuperCall(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A<T1, T2> {\n    constructor(private map: (value: T1) => T2) {\n\n    }\n}\n\nclass B extends A<number, string> {\n    constructor() { super(va/*1*/lue => String(va/*2*/lue.toExpone/*3*/ntial())); }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(parameter) value: number", "");
                f.VerifyQuickInfoAt(t, "2", "(parameter) value: number", "");
                f.VerifyQuickInfoAt(t, "3", "(method) Number.toExponential(fractionDigits?: number): string", "Returns a string containing a number represented in exponential notation.");
            }
            finally
            {
                done();
            }
        });
    }

}
