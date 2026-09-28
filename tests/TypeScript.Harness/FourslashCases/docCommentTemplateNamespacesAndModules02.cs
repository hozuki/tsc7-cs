// ported from: internal/fourslash/tests/docCommentTemplateNamespacesAndModules02_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateNamespacesAndModules02Case
{
    public static void TestDocCommentTemplateNamespacesAndModules02(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*top*/\nnamespace n1.\n    /*n2*/ n2.\n    /*n3*/ n3 {\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "top", 3, "/** */", null!);
                f.VerifyNoJSDocCompletion(t, "n2");
                f.VerifyNoJSDocCompletion(t, "n3");
            }
            finally
            {
                done();
            }
        });
    }

}
