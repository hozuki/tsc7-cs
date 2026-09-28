// ported from: internal/fourslash/tests/tsxCompletion7_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TsxCompletion7Case
{
    public static void TestTsxCompletion7(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: file.tsx\ndeclare namespace JSX {\n    interface Element { }\n    interface IntrinsicElements {\n        div: { ONE: string; TWO: number; }\n    }\n}\nlet y = { ONE: '' };\nvar x = <div {...y} /**/ />;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "TWO", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "ONE", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextMemberDeclaredBySpreadAssignment }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
