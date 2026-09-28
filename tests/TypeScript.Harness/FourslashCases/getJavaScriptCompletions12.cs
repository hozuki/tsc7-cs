// ported from: internal/fourslash/tests/getJavaScriptCompletions12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetJavaScriptCompletions12Case
{
    public static void TestGetJavaScriptCompletions12(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: Foo.js\n/**\n * @param {number} input\n * @param {string} currency\n * @returns {number}\n */\nvar convert = function(input, currency) {\n    switch(currency./*1*/) {\n            case \"USD\":\n            input./*2*/;\n            case \"EUR\":\n                return \"\" + rateToUsd.EUR;\n            case \"CNY\":\n                return {} + rateToUsd.CNY;\n    }\n}\nconvert(1, \"\")./*3*/\n/**\n * @param {number} x\n */\nvar test1 = function(x) { return x./*4*/ }, test2 = function(a) { return a./*5*/ };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "charCodeAt", Kind = LSP.CompletionItemKind.Method }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "3", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "toExponential", Kind = LSP.CompletionItemKind.Method }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "test1", Kind = LSP.CompletionItemKind.Text, SortText = LS.SortTextJavascriptIdentifiers }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
