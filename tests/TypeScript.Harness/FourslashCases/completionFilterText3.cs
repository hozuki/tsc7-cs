// ported from: internal/fourslash/tests/completionFilterText3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionFilterText3Case
{
    public static void TestCompletionFilterText3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ndeclare const foo1: { b: number; \"a bc\": string; };\nif (true) {\n    foo1[|.|]/*1*/\n} \nelse {\n    foo1[|.a|]/*2*/\n}\n\ndeclare const foo2: { b: number; \"a bc\": string; } | undefined;\nif (true) {\n    foo2[|.|]/*3*/\n} else if (false) {\n    foo2[|.a|]/*4*/\n} else {\n    foo2[|?.|]/*5*/\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a bc", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority, InsertText = "[\"a bc\"]", FilterText = ".a bc", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "[\"a bc\"]", Range = f.Ranges()[0].LSRange } } }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a bc", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority, InsertText = "[\"a bc\"]", FilterText = ".a bc", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "[\"a bc\"]", Range = f.Ranges()[1].LSRange } } }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a bc", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority, InsertText = "?.[\"a bc\"]", FilterText = ".a bc", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "?.[\"a bc\"]", Range = f.Ranges()[2].LSRange } } }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a bc", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority, InsertText = "?.[\"a bc\"]", FilterText = ".a bc", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "?.[\"a bc\"]", Range = f.Ranges()[3].LSRange } } }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a bc", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority, InsertText = "?.[\"a bc\"]", FilterText = "?.a bc", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "?.[\"a bc\"]", Range = f.Ranges()[4].LSRange } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
