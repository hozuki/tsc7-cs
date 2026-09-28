// ported from: internal/fourslash/tests/jsxTagNameCompletionUnderElementUnclosed_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsxTagNameCompletionUnderElementUnclosedCase
{
    public static void TestJsxTagNameCompletionUnderElementUnclosed(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\ndeclare namespace JSX {\n    interface IntrinsicElements {\n        button: any;\n        div: any;\n    }\n}\nfunction fn() {\n    return <>\n        <butto/*1*/\n    </>;\n}\nfunction fn2() {\n    return <>\n        preceding junk <butto/*2*/\n    </>;\n}\nfunction fn3() {\n    return <>\n        <butto/*3*/ style=\"\"\n    </>;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "button", Detail = "(property) JSX.IntrinsicElements.button: any" }] }, UserPreferences = new LSUP.UserPreferences { JsxAttributeCompletionStyle = LSU.JsxAttributeCompletionStyleBraces } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "button", Detail = "(property) JSX.IntrinsicElements.button: any" }] }, UserPreferences = new LSUP.UserPreferences { JsxAttributeCompletionStyle = LSU.JsxAttributeCompletionStyleBraces } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "button", Detail = "(property) JSX.IntrinsicElements.button: any" }] }, UserPreferences = new LSUP.UserPreferences { JsxAttributeCompletionStyle = LSU.JsxAttributeCompletionStyleBraces } });
            }
            finally
            {
                done();
            }
        });
    }

}
