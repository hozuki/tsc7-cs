// ported from: internal/fourslash/tests/quickInfoJsDoc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocCase
{
    public static void TestQuickInfoJsDoc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: esnext\n/**\n * A constant\n * @deprecated\n */\nvar foo = \"foo\";\n\n/**\n * A function\n * @deprecated\n */\nfunction fn() { }\n\n/**\n * A class\n * @deprecated\n */\nclass C {\n    /**\n     * A field\n     * @deprecated\n     */\n    field = \"field\";\n\n    /**\n     * A getter\n     * @deprecated\n     */\n    get getter() {\n        return;\n    }\n\n    /**\n     * A method\n     * @deprecated\n     */\n    m() { }\n\n    get a() {\n        this.field/*0*/;\n        this.getter/*1*/;\n        this.m/*2*/;\n        foo/*3*/;\n        C/*4*//;\n        fn()/*5*/;\n\n        return 1;\n    }\n\n    set a(value: number) {\n        this.field/*6*/;\n        this.getter/*7*/;\n        this.m/*8*/;\n        foo/*9*/;\n        C/*10*/;\n        fn/*11*/();\n    }\n}"u8);
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
