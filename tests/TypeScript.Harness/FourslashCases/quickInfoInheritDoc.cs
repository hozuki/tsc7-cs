// ported from: internal/fourslash/tests/quickInfoInheritDoc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoInheritDocCase
{
    public static void TestQuickInfoInheritDoc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noEmit: true\n// @allowJs: true\n// @Filename: quickInfoInheritDoc.ts\nabstract class BaseClass {\n    /**\n     * Useful description always applicable\n     * \n     * @returns {string} Useful description of return value always applicable.\n     */\n    public static doSomethingUseful(stuff?: any): string {\n        throw new Error('Must be implemented by subclass');\n    }\n\n    /**\n     * BaseClass.func1\n     * @param {any} stuff1 BaseClass.func1.stuff1\n     * @returns {void} BaseClass.func1.returns\n     */\n    public static func1(stuff1: any): void {\n    }\n\n    /**\n     * Applicable description always.\n     */\n    public static readonly someProperty: string = 'general value';\n}\n\n\n\n\nclass SubClass extends BaseClass {\n\n    /**\n     * @inheritDoc\n     * \n     * @param {{ tiger: string; lion: string; }} [mySpecificStuff] Description of my specific parameter.\n     */\n    public static /*1*/doSomethingUseful(mySpecificStuff?: { tiger: string; lion: string; }): string {\n        let useful = '';\n\n        // do something useful to useful\n\n        return useful;\n    }\n\n    /**\n     * @inheritDoc\n     * @param {any} stuff1 SubClass.func1.stuff1\n     * @returns {void} SubClass.func1.returns\n     */\n    public static /*2*/func1(stuff1: any): void {\n    }\n\n    /**\n     * text over tag\n     * @inheritDoc\n     * text after tag\n     */\n    public static readonly /*3*/someProperty: string = 'specific to this class value'\n}"u8);
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
