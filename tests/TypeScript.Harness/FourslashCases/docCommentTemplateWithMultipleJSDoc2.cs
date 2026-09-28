// ported from: internal/fourslash/tests/docCommentTemplateWithMultipleJSDoc2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateWithMultipleJSDoc2Case
{
    public static void TestDocCommentTemplateWithMultipleJSDoc2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** @typedef {string} Id */\n\n/** /**/ */\nfunction foo(x, y, z) {}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "", 7, "/**\n * \n * @param x\n * @param y\n * @param z\n */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
