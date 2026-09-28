// ported from: internal/fourslash/tests/exhaustiveCaseCompletions1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ExhaustiveCaseCompletions1Case
{
    public static void TestExhaustiveCaseCompletions1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\nenum E {\n    A = 0,\n    B = \"B\",\n    C = \"C\",\n}\n// Mixed union\ndeclare const u: E.A | E.B | 1;\nswitch (u) {\n    case/*1*/\n}\n// Union enum\ndeclare const e: E;\nswitch (e) {\n    case/*2*/\n}\nenum F {\n    D = 1 << 0,\n    E = 1 << 1,\n    F = 1 << 2,\n}\n\ndeclare const f: F;\nswitch (f) {\n    case/*3*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case 1: ...", InsertText = "case 1:$1\ncase E.A:$2\ncase E.B:$3", SortText = LS.SortTextGlobalsOrKeywords, InsertTextFormat = LSP.InsertTextFormat.Snippet }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case E.A: ...", InsertText = "case E.A:$1\ncase E.B:$2\ncase E.C:$3", SortText = LS.SortTextGlobalsOrKeywords, InsertTextFormat = LSP.InsertTextFormat.Snippet }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case F.D: ...", InsertText = "case F.D:$1\ncase F.E:$2\ncase F.F:$3", SortText = LS.SortTextGlobalsOrKeywords, InsertTextFormat = LSP.InsertTextFormat.Snippet }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
