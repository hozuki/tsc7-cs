// ported from: internal/fourslash/tests/docCommentTemplate_insideEmptyComment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplate_insideEmptyCommentCase
{
    public static void TestDocCommentTemplate_insideEmptyComment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** /**/ */\nfunction f(p) { return p; }\n\n/** Doc/*1*/ */\nfunction g(p) { return p; }"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "", 7, "/**\n * \n * @param p\n * @returns\n */", null!);
                f.VerifyNoJSDocCompletion(t, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
