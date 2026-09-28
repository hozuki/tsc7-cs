// ported from: internal/fourslash/tests/completionsOverridingMethod10_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsOverridingMethod10Case
{
    public static void TestCompletionsOverridingMethod10(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n// @newline: LF\ninterface Base {\n    a: string;\n    b(a: string): void;\n    c(a: string): string;\n    c(a: number): number;\n}\nclass Sub implements Base {\n   /*a*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", InsertText = "a: string;", FilterText = "a", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "b", InsertText = "b(a: string): void {\n}", FilterText = "b", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "c", InsertText = "c(a: string): string;\nc(a: number): number;\nc(a: unknown): string | number {\n}", FilterText = "c", SortText = LS.SortTextLocationPriority }] }, UserPreferences = new LSUP.UserPreferences { IncludeCompletionsWithClassMemberSnippets = TRI.TSTrue } });
            }
            finally
            {
                done();
            }
        });
    }

}
