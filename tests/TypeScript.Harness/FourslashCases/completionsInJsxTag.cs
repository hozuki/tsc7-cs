// ported from: internal/fourslash/tests/completionsInJsxTag_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsInJsxTagCase
{
    public static void TestCompletionsInJsxTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @Filename: /a.tsx\ndeclare namespace JSX {\n    interface Element {}\n    interface IntrinsicElements {\n        div: {\n            /** Doc */\n            foo: string\n            /** Label docs */\n            \"aria-label\": string\n        }\n    }\n}\nclass Foo {\n    render() {\n        <div /*1*/ ></div>;\n        <div  /*2*/ />\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "aria-label", Kind = LSP.CompletionItemKind.Field, Detail = "(property) \"aria-label\": string", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Label docs" } } }, new LSP.CompletionItem { Label = "foo", Kind = LSP.CompletionItemKind.Field, Detail = "(property) foo: string", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Doc" } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestCompletionsInJsxNamespacedIntrinsicTag(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: react\n// @Filename: /a.tsx\ndeclare const React: any;\ndeclare namespace JSX {\n    interface Element {}\n    interface IntrinsicElements {\n        /** Element docs */\n        \"foo:bar\": {\n            /** Foo docs */\n            foo: boolean\n            /** Bar docs */\n            bar: string\n        }\n    }\n}\n<foo:bar /*1*/ />\n<foo:bar  /*2*/></foo:bar>"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "bar", Kind = LSP.CompletionItemKind.Field, Detail = "(property) bar: string", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Bar docs" } } }, new LSP.CompletionItem { Label = "foo", Kind = LSP.CompletionItemKind.Field, Detail = "(property) foo: boolean", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Foo docs" } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
