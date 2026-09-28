// ported from: internal/fourslash/tests/jsxAttributeSnippetCompletionAfterTypeArgs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSU = TypeScript.LsUtil.LsUtil;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsxAttributeSnippetCompletionAfterTypeArgsCase
{
    public static void TestJsxAttributeSnippetCompletionAfterTypeArgs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\n//@Filename: file.tsx\ndeclare const React: any;\n\nnamespace JSX {\n    export interface IntrinsicElements {\n        div: any;\n    }\n}\n\nfunction GenericElement<T>(props: {xyz?: T}) {\n    return <></>\n}\n\nfunction fn1() {\n    return <div>\n        <GenericElement<number> /*1*/ />\n    </div>\n}\n\nfunction fn2() {\n    return <>\n        <GenericElement<number> /*2*/ />\n    </>\n}\nfunction fn3() {\n    return <div>\n        <GenericElement<number> /*3*/ ></GenericElement>\n    </div>\n}\n\nfunction fn4() {\n    return <>\n        <GenericElement<number> /*4*/ ></GenericElement>\n    </>\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "xyz?", InsertText = "xyz={$1}", FilterText = "xyz", Detail = "(property) xyz?: number", InsertTextFormat = LSP.InsertTextFormat.Snippet, SortText = LS.SortTextOptionalMember }] }, UserPreferences = new LSUP.UserPreferences { JsxAttributeCompletionStyle = LSU.JsxAttributeCompletionStyleBraces } });
            }
            finally
            {
                done();
            }
        });
    }

}
