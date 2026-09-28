// ported from: internal/fourslash/tests/quickInfoJsDocGetterSetter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocGetterSetterCase
{
    public static void TestQuickInfoJsDocGetterSetter(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class A {\n    /**\n     * getter A\n     * @returns return A\n     */\n    get /*1*/x(): string {\n        return \"\";\n    }\n    /**\n     * setter A\n     * @param value foo A\n     * @todo empty jsdoc\n     */\n    set /*2*/x(value) { }\n}\n// override both getter and setter\nclass B extends A {\n    /**\n     * getter B\n     * @returns return B\n     */\n    get /*3*/x(): string {\n        return \"\";\n    }\n    /**\n     * setter B\n     * @param value foo B\n     */\n    set /*4*/x(vale) { }\n}\n// not override\nclass C extends A { }\n// only override setter\nclass D extends A {\n    /**\n     * setter D\n     * @param value foo D\n     */\n    set /*5*/x(val: string) { }\n}\nnew A()./*6*/x = \"1\";\nnew B()./*7*/x = \"1\";\nnew C()./*8*/x = \"1\";\nnew D()./*9*/x = \"1\";"u8);
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
