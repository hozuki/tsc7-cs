// ported from: internal/fourslash/tests/completionsOverridingMethodCrash2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethodCrash2Case
{
    public static void TestCompletionsOverridingMethodCrash2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"lib\": [\"es5\"]\n  }\n}\n// @Filename: /home/src/workspaces/project/utils.ts\nexport class Element {\n    // ...\n}\n\nexport abstract class Component {\n    abstract render(): Element;\n}\n// @Filename: /home/src/workspaces/project/classes.ts\nimport { Component } from \"./utils.js\";\n\nexport class MyComponent extends Component {\n    [|render/**/|]\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "render", TextEdit = U.InsertReplaceTextEdit("render(): Element {\n    $0\n}", f.Ranges()[0].LSRange), FilterText = "render", SortText = LS.SortTextLocationPriority, InsertTextFormat = LSP.InsertTextFormat.Snippet, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.GoToFile(t, "/home/src/workspaces/project/utils.ts");
                f.GoToMarker(t, "");
                f.Backspace(t, 1);
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "render", TextEdit = U.InsertReplaceTextEdit("render(): Element {\n    $0\n}", f.Ranges()[0].LSRange), FilterText = "render", SortText = LS.SortTextLocationPriority, InsertTextFormat = LSP.InsertTextFormat.Snippet, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
