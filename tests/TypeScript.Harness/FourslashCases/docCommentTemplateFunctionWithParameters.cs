// ported from: internal/fourslash/tests/docCommentTemplateFunctionWithParameters_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateFunctionWithParametersCase
{
    public static void TestDocCommentTemplateFunctionWithParameters(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: functionWithParams.ts\n/*0*/\n    /*1*/\n        function foo(x: number, y: string): boolean {}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.GoToMarker(t, "0");
                f.VerifyJSDocCompletion(t, "0", 7, "/**\n * \n * @param x\n * @param y\n */", null!);
                f.VerifyJSDocCompletion(t, "1", 0, "/**\n     * \n     * @param x\n     * @param y\n     */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
