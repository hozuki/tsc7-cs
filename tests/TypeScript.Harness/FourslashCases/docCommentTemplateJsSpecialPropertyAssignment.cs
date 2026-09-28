// ported from: internal/fourslash/tests/docCommentTemplateJsSpecialPropertyAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateJsSpecialPropertyAssignmentCase
{
    public static void TestDocCommentTemplateJsSpecialPropertyAssignment(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /a.js\n/*0*/module.exports = function(a) {};\nconst myNamespace  = {};\n/*1*/myNamespace.myExport = function(x) {};"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "0", 7, "/**\n * \n * @param {any} a\n */\n", null!);
                f.VerifyJSDocCompletion(t, "1", 7, "/**\n * \n * @param {any} x\n */\n", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
