// ported from: internal/fourslash/tests/docCommentTemplateConstructor01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateConstructor01Case
{
    public static void TestDocCommentTemplateConstructor01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    private p;\n    /*0*/\n    constructor(a, b, c, d);\n    /*1*/\n    constructor(public a, private b, protected c, d, e?) {\n    }\n\n    foo();\n    foo(a?, b?, ...args) {\n    }\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "0", 11, "/**\n     * \n     * @param a\n     * @param b\n     * @param c\n     * @param d\n     */", null!);
                f.VerifyJSDocCompletion(t, "1", 11, "/**\n     * \n     * @param a\n     * @param b\n     * @param c\n     * @param d\n     * @param e\n     */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
