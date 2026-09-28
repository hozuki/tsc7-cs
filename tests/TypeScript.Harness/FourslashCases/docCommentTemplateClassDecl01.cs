// ported from: internal/fourslash/tests/docCommentTemplateClassDecl01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateClassDecl01Case
{
    public static void TestDocCommentTemplateClassDecl01(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*decl*/class C {\n    private p;\n    constructor(a, b, c, d);\n    constructor(public a, private b, protected c, d, e?) {\n    }\n\n    foo();\n    foo(a?, b?, ...args) {\n    }\n}"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "decl", 3, "/** */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
