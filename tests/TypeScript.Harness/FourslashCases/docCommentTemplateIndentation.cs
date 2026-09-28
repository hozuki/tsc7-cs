// ported from: internal/fourslash/tests/docCommentTemplateIndentation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateIndentationCase
{
    public static void TestDocCommentTemplateIndentation(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: indents.ts\n    a   /*2*/\n    /*1*/\n/*0*/        function foo() { }"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "0", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "1", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "2", 3, "/** */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
