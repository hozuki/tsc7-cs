// ported from: internal/fourslash/tests/completionFilterText1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionFilterText1Case
{
    public static void TestCompletionFilterText1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\nclass Foo1 {\n    #bar: number;\n    constructor(bar: number) {\n        this.[|b|]/*1*/\n    }\n}\n\nclass Foo5 {\n\t#bar: number;\n\tconstructor(bar: number) {\n\t\tthis./*5*/\n\t}\n}\n\nclass Foo2 {\n    #bar: number;\n    constructor(bar: number) {\n        this.[|#b|]/*2*/\n    }\n}\n\nclass Foo6 {\n    #bar: number;\n    constructor(bar: number) {\n        this.[|#|]/*6*/\n    }\n}\n\nclass Foo3 {\n    #bar: number;\n    constructor(bar: number) {\n       [|b|]/*3*/\n    }\n}\n\nclass Foo7 {\n\t#bar: number;\n\tconstructor(bar: number) {\n\t   /*7*/\n\t}\n}\n\nclass Foo4 {\n    #bar: number;\n    constructor(bar: number) {\n       [|#b|]/*4*/\n    }\n}\n\nclass Foo8 {\n    #bar: number;\n    constructor(bar: number) {\n       [|#|]/*8*/\n    }\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = new F.EditRange { Insert = f.Ranges()[0], Replace = f.Ranges()[0] } }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority, FilterText = "bar" }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority, FilterText = "bar" }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = new F.EditRange { Insert = f.Ranges()[1], Replace = f.Ranges()[1] } }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority }] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = new F.EditRange { Insert = f.Ranges()[2], Replace = f.Ranges()[2] } }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextSuggestedClassMembers, FilterText = "bar", TextEdit = new LSP.TextEditOrInsertReplaceEdit { InsertReplaceEdit = new LSP.InsertReplaceEdit { NewText = "this.#bar", Insert = f.Ranges()[3].LSRange, Replace = f.Ranges()[3].LSRange } } }] } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextSuggestedClassMembers, FilterText = "bar", InsertText = "this.#bar" }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextSuggestedClassMembers, TextEdit = new LSP.TextEditOrInsertReplaceEdit { InsertReplaceEdit = new LSP.InsertReplaceEdit { NewText = "this.#bar", Insert = f.Ranges()[4].LSRange, Replace = f.Ranges()[4].LSRange } } }] } });
                f.VerifyCompletions(t, "8", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "#bar", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextSuggestedClassMembers, TextEdit = new LSP.TextEditOrInsertReplaceEdit { InsertReplaceEdit = new LSP.InsertReplaceEdit { NewText = "this.#bar", Insert = f.Ranges()[5].LSRange, Replace = f.Ranges()[5].LSRange } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
