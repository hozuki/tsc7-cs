// ported from: internal/fourslash/tests/completionEntryForUnionProperty2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionEntryForUnionProperty2Case
{
    public static void TestCompletionEntryForUnionProperty2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\ninterface One {\n    commonProperty: string;\n    commonFunction(): number;\n    anotherProperty: Record<string, number>;\n}\n\ninterface Two {\n    commonProperty: number;\n    commonFunction(): number;\n    anotherProperty: { foo: number }\n}\n\nvar x : One | Two;\n\nx.commonProperty./*1*/;\nx.anotherProperty./*2*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "toLocaleString", Detail = "(method) toLocaleString(): string (+1 overload)", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Returns a date converted to a string using the current locale." } } }, new LSP.CompletionItem { Label = "toString", Detail = "(method) toString(): string (+1 overload)", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Returns a string representation of a string." } } }, new LSP.CompletionItem { Label = "valueOf", Detail = "(method) valueOf(): string | number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Returns the primitive value of the specified object." } } }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
