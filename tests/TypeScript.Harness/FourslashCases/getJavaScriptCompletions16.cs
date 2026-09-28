// ported from: internal/fourslash/tests/getJavaScriptCompletions16_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetJavaScriptCompletions16Case
{
    public static void TestGetJavaScriptCompletions16(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: file.js\n\"use strict\";\n\nclass Something {\n\n    /**\n     * @param {number} a\n     */\n    constructor(a, b) {\n        a/*body*/\n    }\n\n    /**\n     * @param {number} a\n     */\n    method(a) {\n        a/*method*/\n    }\n}\nlet x = new Something(/*sig*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "body");
                f.Insert(t, ".");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "toFixed", Kind = LSP.CompletionItemKind.Method }] } });
                f.Backspace(t, 1);
                f.GoToMarker(t, "sig");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { Text = "Something(a: number, b: any): Something" });
                f.GoToMarker(t, "method");
                f.Insert(t, ".");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "toFixed", Kind = LSP.CompletionItemKind.Method }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
