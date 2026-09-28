// ported from: internal/fourslash/tests/completionsOverridingMethod0_test.go
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

internal static class CompletionsOverridingMethod0Case
{
    public static void TestCompletionsOverridingMethod0(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: a.ts\nabstract class ABase {\n    abstract foo(param1: string, param2: boolean): Promise<void>;\n}\n\nclass ASub extends ABase {\n    [|f/*a*/|]\n}\n// @Filename: b.ts\nclass BBase {\n    foo(a: string, b: string): string {\n        return a + b;\n    }\n}\n\nclass BSub extends BBase {\n    [|f/*b*/|]\n}\n// @Filename: c.ts\nclass CBase {\n    foo(a: string | number): string {\n        return a + \"\";\n    }\n}\n\nclass CSub extends CBase {\n    foo(a: string): string {\n        return add;\n    }\n}\n\nclass CSub2 extends CSub {\n    [|f/*c*/|]\n}\n// @Filename: d.ts\nabstract class DBase {\n    abstract foo(a: string): string;\n}\n\nabstract class DSub extends DBase {\n    [|f/*d*/|]\n}\n// @Filename: e.ts\ninterface EBase {\n    foo(a: string): string;\n}\n\nclass ESub implements EBase {\n    [|f/*e*/|]\n}\n// @Filename: f.ts\ninterface FBase {\n    foo(a: string): string;\n}\n\nabstract class FSub implements FBase {\n    [|f/*f*/|]\n}\n// @Filename: g.ts\ninterface GBase {\n    foo(a: string): string;\n    foo(a: undefined, b: number): string;\n}\n\nclass GSub implements GBase {\n    [|f/*g*/|]\n}\n// @Filename: h.ts\nclass HBase {\n    static met(n: number): number {\n        return n;\n    }\n}\n\nclass HSub extends HBase {\n    /*h1*/\n    static /*h2*/\n}\n// @Filename: i.ts\nclass IBase {\n    met<T>(t: T): T {\n        return t;\n    }\n    metcons<T extends string | number>(t: T): T {\n        return t;\n    }\n}\n\nclass ISub extends IBase {\n    /*i*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", TextEdit = U.InsertReplaceTextEdit("foo(param1: string, param2: boolean): Promise<void> {\n}", f.Ranges()[0].LSRange), FilterText = "foo", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "b", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", TextEdit = U.InsertReplaceTextEdit("foo(a: string, b: string): string {\n}", f.Ranges()[1].LSRange), FilterText = "foo", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "c", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", TextEdit = U.InsertReplaceTextEdit("foo(a: string): string {\n}", f.Ranges()[2].LSRange), FilterText = "foo", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "d", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", TextEdit = U.InsertReplaceTextEdit("foo(a: string): string {\n}", f.Ranges()[3].LSRange), FilterText = "foo", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "e", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", TextEdit = U.InsertReplaceTextEdit("foo(a: string): string {\n}", f.Ranges()[4].LSRange), FilterText = "foo", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "f", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", TextEdit = U.InsertReplaceTextEdit("foo(a: string): string {\n}", f.Ranges()[5].LSRange), FilterText = "foo", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "g", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", TextEdit = U.InsertReplaceTextEdit("foo(a: string): string;\nfoo(a: undefined, b: number): string;\nfoo(a: unknown, b?: unknown): string {\n}", f.Ranges()[6].LSRange), FilterText = "foo", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "h1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["met"] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "h2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "met", InsertText = "static met(n: number): number {\n}", FilterText = "met", SortText = LS.SortTextLocationPriority, AdditionalTextEdits = F.FourslashTest.AnyTextEdits, Data = new LSP.CompletionItemData { Source = "ClassMemberSnippet/" } }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
                f.VerifyCompletions(t, "i", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "met", InsertText = "met<T>(t: T): T {\n}", FilterText = "met", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "metcons", InsertText = "metcons<T extends string | number>(t: T): T {\n}", FilterText = "metcons", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
