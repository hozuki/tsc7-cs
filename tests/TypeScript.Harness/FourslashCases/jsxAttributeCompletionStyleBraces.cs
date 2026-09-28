// ported from: internal/fourslash/tests/jsxAttributeCompletionStyleBraces_test.go
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

internal static class JsxAttributeCompletionStyleBracesCase
{
    public static void TestJsxAttributeCompletionStyleBraces(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: foo.tsx\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n        foo: {\n            prop_a: boolean;\n            prop_b: string;\n            prop_c: any;\n            prop_d: { p1: string; }\n            prop_e: string | undefined;\n            prop_f: boolean | undefined | { p1: string; };\n            prop_g: { p1: string; } | undefined;\n            prop_h?: string;\n            prop_i?: boolean;\n            prop_j?: { p1: string; };\n        }\n    }\n}\n\n<foo [|prop_/**/|] />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "prop_a", TextEdit = U.InsertReplaceTextEdit("prop_a={$1}", f.Ranges()[0].LSRange), InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "prop_b", TextEdit = U.InsertReplaceTextEdit("prop_b={$1}", f.Ranges()[0].LSRange), InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "prop_c", TextEdit = U.InsertReplaceTextEdit("prop_c={$1}", f.Ranges()[0].LSRange), InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "prop_d", TextEdit = U.InsertReplaceTextEdit("prop_d={$1}", f.Ranges()[0].LSRange), InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "prop_e", TextEdit = U.InsertReplaceTextEdit("prop_e={$1}", f.Ranges()[0].LSRange), InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "prop_f", TextEdit = U.InsertReplaceTextEdit("prop_f={$1}", f.Ranges()[0].LSRange), InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "prop_g", TextEdit = U.InsertReplaceTextEdit("prop_g={$1}", f.Ranges()[0].LSRange), InsertTextFormat = LSP.InsertTextFormat.Snippet }, new LSP.CompletionItem { Label = "prop_h?", TextEdit = U.InsertReplaceTextEdit("prop_h={$1}", f.Ranges()[0].LSRange), FilterText = "prop_h", InsertTextFormat = LSP.InsertTextFormat.Snippet, SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "prop_i?", TextEdit = U.InsertReplaceTextEdit("prop_i={$1}", f.Ranges()[0].LSRange), FilterText = "prop_i", InsertTextFormat = LSP.InsertTextFormat.Snippet, SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "prop_j?", TextEdit = U.InsertReplaceTextEdit("prop_j={$1}", f.Ranges()[0].LSRange), FilterText = "prop_j", InsertTextFormat = LSP.InsertTextFormat.Snippet, SortText = LS.SortTextOptionalMember }] }, UserPreferences = new LSUP.UserPreferences { JsxAttributeCompletionStyle = LSU.JsxAttributeCompletionStyleBraces } });
            }
            finally
            {
                done();
            }
        });
    }

}
