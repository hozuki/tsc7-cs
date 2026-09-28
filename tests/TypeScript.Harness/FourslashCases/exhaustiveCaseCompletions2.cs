// ported from: internal/fourslash/tests/exhaustiveCaseCompletions2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ExhaustiveCaseCompletions2Case
{
    public static void TestExhaustiveCaseCompletions2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @newline: LF\n// @Filename: /dep.ts\nexport enum E {\n    A = 0,\n    B = \"B\",\n    C = \"C\",\n}\ndeclare const u: E.A | E.B | 1;\nexport { u };\n// @Filename: /main.ts\nimport { u } from \"./dep\";\nswitch (u) {\n    case/*1*/\n}\n// @Filename: /other.ts\nimport * as d from \"./dep\";\ndeclare const u: d.E;\nswitch (u) {\n    case/*2*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, F.FourslashTest.GetDefaultCapabilitiesWithOptions(new F.ClientCapabilitiesOptions { CompletionItem = new LSP.ClientCompletionItemOptions { SnippetSupport = true } }), content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case 1: ...", InsertText = "case 1:$1\ncase E.A:$2\ncase E.B:$3", InsertTextFormat = LSP.InsertTextFormat.Snippet, SortText = LS.SortTextGlobalsOrKeywords, AdditionalTextEdits = F.FourslashTest.AnyTextEdits }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "case d.E.A: ...", InsertText = "case d.E.A:$1\ncase d.E.B:$2\ncase d.E.C:$3", InsertTextFormat = LSP.InsertTextFormat.Snippet, SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyApplyCodeActionFromCompletion(t, "1", new F.ApplyCodeActionFromCompletionOptions { Name = "case 1: ...", Source = "SwitchCases/", NewFileContent = "import { E, u } from \"./dep\";\nswitch (u) {\n    case\n}" });
            }
            finally
            {
                done();
            }
        });
    }

}
