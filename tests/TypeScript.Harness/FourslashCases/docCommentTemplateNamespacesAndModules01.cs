// ported from: internal/fourslash/tests/docCommentTemplateNamespacesAndModules01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateNamespacesAndModules01Case
{
    public static void TestDocCommentTemplateNamespacesAndModules01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*namespaceN*/\nnamespace n {\n}\n\n/*namespaceM*/\nnamespace m {\n}\n\n/*ambientModule*/\nmodule \"ambientModule\" {\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "namespaceN", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "namespaceM", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "ambientModule", 3, "/** */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
