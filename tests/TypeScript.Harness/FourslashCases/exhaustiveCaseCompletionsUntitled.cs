// ported from: internal/fourslash/tests/exhaustiveCaseCompletionsUntitled_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ExhaustiveCaseCompletionsUntitledCase
{
    public static void TestExhaustiveCaseCompletionsUntitledLocalEnum(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @filename: ^/untitled/ts-nul-authority/Untitled-1.ts\nenum E {\n    A = \"A\",\n    B = \"B\",\n    C = \"C\",\n}\ndeclare const e: E;\nswitch (e) {\n    case/**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case E.A: ...", InsertText = "case E.A:$1\ncase E.B:$2\ncase E.C:$3", SortText = LS.SortTextGlobalsOrKeywords, InsertTextFormat = LSP.InsertTextFormat.Snippet }] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestExhaustiveCaseCompletionsUntitledGlobalEnum(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @filename: /home/src/project/globals.d.ts\ndeclare enum Direction {\n\tUp = \"Up\",\n\tDown = \"Down\",\n\tLeft = \"Left\",\n\tRight = \"Right\",\n}\ndeclare const direction: Direction;\n\n// @filename: ^/untitled/ts-nul-authority/Untitled-1.ts\nswitch (direction) {\n    case/**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case Direction.Up: ...", InsertText = "case Direction.Up:$1\ncase Direction.Down:$2\ncase Direction.Left:$3\ncase Direction.Right:$4", SortText = LS.SortTextGlobalsOrKeywords, InsertTextFormat = LSP.InsertTextFormat.Snippet }] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestExhaustiveCaseCompletionsUntitledStringLiterals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @filename: ^/untitled/ts-nul-authority/Untitled-1.ts\nexport {};\ndeclare const status: \"pending\" | \"success\" | \"error\";\nswitch (status) {\n    case/**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case \"error\": ...", InsertText = "case \"error\":$1" + "\n" + "case \"pending\":$2" + "\n" + "case \"success\":$3", SortText = LS.SortTextGlobalsOrKeywords, InsertTextFormat = LSP.InsertTextFormat.Snippet }] } });
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestExhaustiveCaseCompletionsUntitledImportedEnum(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @filename: /home/src/project/enums.ts\nexport enum Status {\n    Active,\n    Inactive,\n    Pending,\n}\n\n// @filename: ^/untitled/ts-nul-authority/Untitled-1.ts\ndeclare const s: import(\"/home/src/project/enums\").Status;\nswitch (s) {\n    case/**/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case Status.Active: ...", InsertText = "case Status.Active:$1\ncase Status.Inactive:$2\ncase Status.Pending:$3", SortText = LS.SortTextGlobalsOrKeywords, InsertTextFormat = LSP.InsertTextFormat.Snippet }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
