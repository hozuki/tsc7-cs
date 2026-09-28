// ported from: internal/fourslash/tests/jsxAttributeCompletionStyleNone_test.go
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

internal static class JsxAttributeCompletionStyleNoneCase
{
    public static void TestJsxAttributeCompletionStyleNone(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: foo.tsx\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n        foo: {\n            prop_a: boolean;\n            prop_b: string;\n            prop_c: any;\n            prop_d: { p1: string; }\n            prop_e: string | undefined;\n            prop_f: boolean | undefined | { p1: string; };\n            prop_g: { p1: string; } | undefined;\n            prop_h?: string;\n            prop_i?: boolean;\n            prop_j?: { p1: string; };\n        }\n    }\n}\n\n<foo [|prop_/**/|] />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "prop_a" }, new LSP.CompletionItem { Label = "prop_b" }, new LSP.CompletionItem { Label = "prop_c" }, new LSP.CompletionItem { Label = "prop_d" }, new LSP.CompletionItem { Label = "prop_e" }, new LSP.CompletionItem { Label = "prop_f" }, new LSP.CompletionItem { Label = "prop_g" }, new LSP.CompletionItem { Label = "prop_h?", TextEdit = U.InsertReplaceTextEdit("prop_h", f.Ranges()[0].LSRange), FilterText = "prop_h", SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "prop_i?", TextEdit = U.InsertReplaceTextEdit("prop_i", f.Ranges()[0].LSRange), FilterText = "prop_i", SortText = LS.SortTextOptionalMember }, new LSP.CompletionItem { Label = "prop_j?", TextEdit = U.InsertReplaceTextEdit("prop_j", f.Ranges()[0].LSRange), FilterText = "prop_j", SortText = LS.SortTextOptionalMember }] }, UserPreferences = new LSUP.UserPreferences { JsxAttributeCompletionStyle = LSU.JsxAttributeCompletionStyleNone } });
            }
            finally
            {
                done();
            }
        });
    }

}
