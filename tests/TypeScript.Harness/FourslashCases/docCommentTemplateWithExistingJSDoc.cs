// ported from: internal/fourslash/tests/docCommentTemplateWithExistingJSDoc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateWithExistingJSDocCase
{
    public static void TestDocCommentTemplateWithExistingJSDoc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** /**/ */\n\n/**\n * @param {string} a\n * @param {string} b\n */\nfunction foo(a, b) {\n    return a + b;\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyNoJSDocCompletion(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
