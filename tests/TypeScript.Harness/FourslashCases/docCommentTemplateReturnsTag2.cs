// ported from: internal/fourslash/tests/docCommentTemplateReturnsTag2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateReturnsTag2Case
{
    public static void TestDocCommentTemplateReturnsTag2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*0*/\nfunction f1(x: number, y: number) {\n    return 1;\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "0", 7, "/**\n * \n * @param x\n * @param y\n * @returns\n */", true);
                f.VerifyJSDocCompletion(t, "0", 7, "/**\n * \n * @param x\n * @param y\n */", false);
            }
            finally
            {
                done();
            }
        });
    }

}
