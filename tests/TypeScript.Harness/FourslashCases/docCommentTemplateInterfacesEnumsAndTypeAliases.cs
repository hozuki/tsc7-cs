// ported from: internal/fourslash/tests/docCommentTemplateInterfacesEnumsAndTypeAliases_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class DocCommentTemplateInterfacesEnumsAndTypeAliasesCase
{
    public static void TestDocCommentTemplateInterfacesEnumsAndTypeAliases(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*interfaceFoo*/\ninterface Foo {\n    /*propertybar*/\n    bar: any;\n\n    /*methodbaz*/\n    baz(message: any): void;\n\n    /*methodUnit*/\n    unit(): void;\n}\n\n/*enumStatus*/\nconst enum Status {\n    /*memberOpen*/\n    Open,\n\n    /*memberClosed*/\n    Closed\n}\n\n/*aliasBar*/\ntype Bar = Foo & any;"u8);
            var capabilities = F.FourslashTest.GetDefaultCapabilities();
            capabilities!.TextDocument!.Completion!.CompletionItem!.SnippetSupport = false;
            var (f, done) = F.Fourslash.NewFourslash(t, capabilities, content);
            try
            {
                f.VerifyJSDocCompletion(t, "interfaceFoo", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "propertybar", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "methodbaz", 11, "/**\n     * \n     * @param message\n     */", null!);
                f.VerifyJSDocCompletion(t, "methodUnit", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "enumStatus", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "memberOpen", 3, "/** */", null!);
                f.VerifyJSDocCompletion(t, "memberClosed", 3, "/** */", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
