// ported from: internal/fourslash/tests/quickInfoJsDocTags3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTags3Case
{
    public static void TestQuickInfoJsDocTags3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: quickInfoJsDocTags3.ts\ninterface Foo {\n    /**\n     * comment\n     * @author Me <me@domain.tld>\n     * @see x (the parameter)\n     * @param {number} x - x comment\n     * @param {number} y - y comment\n     * @throws {Error} comment\n     */\n    method(x: number, y: number): void;\n}\n\nclass Bar implements Foo {\n    /**/method(): void {\n        throw new Error(\"Method not implemented.\");\n    }\n}"u8);
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
