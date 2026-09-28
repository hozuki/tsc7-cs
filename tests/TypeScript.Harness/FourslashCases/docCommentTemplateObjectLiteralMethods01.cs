// ported from: internal/fourslash/tests/docCommentTemplateObjectLiteralMethods01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateObjectLiteralMethods01Case
{
    public static void TestDocCommentTemplateObjectLiteralMethods01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = {\n    /*0*/\n    foo() {\n        return undefined;\n    }\n\n    /*1*/\n    [1 + 2 + 3 + Math.rand()](x: number, y: string, z = true) { }\n\n    /*2*/\n    m1: function(a) {}\n\n    /*3*/\n    m2: (a: string, b: string) => {}\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "0", 11, "/**\n     * \n     * @returns\n     */", null!);
                f.VerifyJSDocCompletion(t, "1", 11, "/**\n     * \n     * @param x\n     * @param y\n     * @param z\n     */", null!);
                f.VerifyJSDocCompletion(t, "2", 11, "/**\n     * \n     * @param a\n     */", null!);
                f.VerifyJSDocCompletion(t, "3", 11, "/**\n     * \n     * @param a\n     * @param b\n     */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
