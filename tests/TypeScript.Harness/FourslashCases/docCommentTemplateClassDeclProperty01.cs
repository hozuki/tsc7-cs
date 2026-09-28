// ported from: internal/fourslash/tests/docCommentTemplateClassDeclProperty01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateClassDeclProperty01Case
{
    public static void TestDocCommentTemplateClassDeclProperty01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n    /** /*0*/  */\n    foo = (p0) => {\n        return p0;\n    };\n    /*1*/\n    bar = (p1) => {\n        return p1;\n    }\n    /*2*/\n    baz = function (p2, p3) {\n        return p2;\n    }\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "0", 11, "/**\n     * \n     * @param p0\n     * @returns\n     */", null!);
                f.VerifyJSDocCompletion(t, "1", 11, "/**\n     * \n     * @param p1\n     * @returns\n     */", null!);
                f.VerifyJSDocCompletion(t, "2", 11, "/**\n     * \n     * @param p2\n     * @param p3\n     * @returns\n     */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
