// ported from: internal/fourslash/tests/quickInfoJSDocTags_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJSDocTagsCase
{
    public static void TestQuickInfoJSDocTags(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/**\n * This is class Foo.\n * @mytag comment1 comment2\n */\nclass Foo {\n    /**\n     * This is the constructor.\n     * @myjsdoctag this is a comment\n     */\n    constructor(value: number) {}\n    /**\n     * method1 documentation\n     * @mytag comment1 comment2\n     */\n    static method1() {}\n    /**\n     * @mytag\n     */\n    method2() {}\n    /**\n     * @mytag comment1 comment2\n     */\n    property1: string;\n    /**\n     * @mytag1 some comments\n     * some more comments about mytag1\n     * @mytag2\n     * here all the comments are on a new line\n     * @mytag3\n     * @mytag\n     */\n    property2: number;\n    /**\n     * @returns {number} a value\n     */\n    method3(): number { return 3; }\n    /**\n     * @param {string} foo A value.\n     * @returns {number} Another value\n     * @mytag\n     */\n    method4(foo: string): number { return 3; }\n    /** @mytag */\n    method5() {}\n    /** method documentation\n     *  @mytag a JSDoc tag\n     */\n    newMethod() {}\n}\nvar foo = new /*1*/Foo(/*10*/4);\n/*2*/Foo./*3*/method1(/*11*/);\nfoo./*4*/method2(/*12*/);\nfoo./*5*/method3(/*13*/);\nfoo./*6*/method4();\nfoo./*7*/property1;\nfoo./*8*/property2;\nfoo./*9*/method5();\nfoo.newMet/*14*/"u8);
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
