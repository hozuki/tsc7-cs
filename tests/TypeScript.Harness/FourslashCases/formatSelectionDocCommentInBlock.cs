// ported from: internal/fourslash/tests/formatSelectionDocCommentInBlock_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSelectionDocCommentInBlockCase
{
    public static void TestFormatSelectionDocCommentInBlock(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("{\n    /*1*//**\n     * Some doc comment\n     *//*2*/\n    const a = 1;\n}\n\nwhile (true) {\n/*3*//**\n * Some doc comment\n *//*4*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatSelection(t, "1", "2");
                f.VerifyCurrentFileContent(t, "{\n    /**\n     * Some doc comment\n     */\n    const a = 1;\n}\n\nwhile (true) {\n/**\n * Some doc comment\n */\n}");
                f.FormatSelection(t, "3", "4");
                f.VerifyCurrentFileContent(t, "{\n    /**\n     * Some doc comment\n     */\n    const a = 1;\n}\n\nwhile (true) {\n    /**\n     * Some doc comment\n     */\n}");
            }
            finally
            {
                done();
            }
        });
    }

}
