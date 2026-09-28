// ported from: internal/fourslash/tests/docCommentTemplateReturnsTag1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateReturnsTag1Case
{
    public static void TestDocCommentTemplateReturnsTag1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*0*/\nfunction f1() {}\n/*1*/\nfunction f2() {\n    return 1;\n}\n/*2*/\nconst f3 = () => 1;\n/*3*/\nconst f3 = () => {\n    return 1;\n}\nclass Foo {\n    /*4*/\n    m1() {}\n\n    /*5*/\n    m2() {\n       return 1;\n    }\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "0", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "1", 7, "/**\n * \n * @returns\n */", null!);
                f.VerifyJSDocCompletion(t, "2", 7, "/**\n * \n * @returns\n */", null!);
                f.VerifyJSDocCompletion(t, "3", 7, "/**\n * \n * @returns\n */", null!);
                f.VerifyJSDocCompletion(t, "4", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "5", 11, "/**\n     * \n     * @returns\n     */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
