// ported from: internal/fourslash/tests/completionsPropertiesPriorities_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsPropertiesPrioritiesCase
{
    public static void TestCompletionsPropertiesPriorities(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\ninterface I {\n  B?: number;\n  a: number;\n  c?: string;\n  d: string\n}\nconst foo = {\n  a: 1,\n  B: 2\n}\nconst i: I = {\n  ...foo,\n  /*a*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["a"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "d", SortText = LS.SortTextLocationPriority, Kind = LSP.CompletionItemKind.Field }, new LSP.CompletionItem { Label = "c?", InsertText = "c", FilterText = "c", SortText = LS.SortTextOptionalMember, Kind = LSP.CompletionItemKind.Field }, new LSP.CompletionItem { Label = "a", SortText = LS.SortTextMemberDeclaredBySpreadAssignment, Kind = LSP.CompletionItemKind.Field }, new LSP.CompletionItem { Label = "B?", InsertText = "B", FilterText = "B", SortText = LS.SortTextMemberDeclaredBySpreadAssignment, Kind = LSP.CompletionItemKind.Field }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
