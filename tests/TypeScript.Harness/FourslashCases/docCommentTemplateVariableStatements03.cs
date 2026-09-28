// ported from: internal/fourslash/tests/docCommentTemplateVariableStatements03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateVariableStatements03Case
{
    public static void TestDocCommentTemplateVariableStatements03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*a*/\nvar a = x => x\n\n/*b*/\nlet b = (x,y,z) => x + y + z;\n\n/*c*/\nconst c = ((x => +x))\n\n/*d*/\nlet d = (function () { })\n\n/*e*/\nlet e = function e([a,b,c]) {\n    return \"hello\"\n};\n\n/*f*/\nlet f = class {\n}\n\n/*g*/\nconst g = ((class G {\n    constructor(private x);\n    constructor(x,y,z);\n    constructor(x,y,z, ...okayThatsEnough) {\n    }\n}))"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "a", 7, "/**\n * \n * @param x\n * @returns\n */", null!);
                f.VerifyJSDocCompletion(t, "b", 7, "/**\n * \n * @param x\n * @param y\n * @param z\n * @returns\n */", null!);
                f.VerifyJSDocCompletion(t, "c", 7, "/**\n * \n * @param x\n * @returns\n */", null!);
                f.VerifyJSDocCompletion(t, "d", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "e", 7, "/**\n * \n * @param param0\n * @returns\n */", null!);
                f.VerifyJSDocCompletion(t, "f", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "g", 7, "/**\n * \n * @param x\n */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
