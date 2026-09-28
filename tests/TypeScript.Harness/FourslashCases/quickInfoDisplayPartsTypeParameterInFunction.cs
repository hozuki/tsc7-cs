// ported from: internal/fourslash/tests/quickInfoDisplayPartsTypeParameterInFunction_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoDisplayPartsTypeParameterInFunctionCase
{
    public static void TestQuickInfoDisplayPartsTypeParameterInFunction(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function /*1*/foo</*2*/U>(/*3*/a: /*4*/U) {\n    return /*5*/a;\n}\n/*6*/foo(\"Hello\");\nfunction /*7*/foo2</*8*/U extends string>(/*9*/a: /*10*/U) {\n    return /*11*/a;\n}\n/*12*/foo2(\"hello\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
