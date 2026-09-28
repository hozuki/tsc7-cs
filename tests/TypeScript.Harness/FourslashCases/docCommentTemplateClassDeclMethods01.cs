// ported from: internal/fourslash/tests/docCommentTemplateClassDeclMethods01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateClassDeclMethods01Case
{
    public static void TestDocCommentTemplateClassDeclMethods01(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class C {\n/*0*/    /*1*/\n    foo();\n    /*2*/foo(a);\n    /*3*/foo(a, b);\n    /*4*/foo(a, {x: string}, [c]);\n    /*5*/foo(a?, b?, ...args) {\n    }\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "0", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "1", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "2", 11, "/**\n     * \n     * @param a\n     */\n    ", null!);
                f.VerifyJSDocCompletion(t, "3", 11, "/**\n     * \n     * @param a\n     * @param b\n     */\n    ", null!);
                f.VerifyJSDocCompletion(t, "4", 11, "/**\n     * \n     * @param a\n     * @param param1\n     * @param param2\n     */\n    ", null!);
                f.VerifyJSDocCompletion(t, "5", 11, "/**\n     * \n     * @param a\n     * @param b\n     * @param args\n     */\n    ", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
