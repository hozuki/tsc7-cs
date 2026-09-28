// ported from: internal/fourslash/tests/completionsGenericTypeWithMultipleBases1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsGenericTypeWithMultipleBases1Case
{
    public static void TestCompletionsGenericTypeWithMultipleBases1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export interface iBaseScope {\n    watch: () => void;\n}\nexport interface iMover {\n    moveUp: () => void;\n}\nexport interface iScope<TModel> extends iBaseScope, iMover {\n    family: TModel;\n}\nvar x: iScope<number>;\nx./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "family", Detail = "(property) iScope<number>.family: number" }, new LSP.CompletionItem { Label = "moveUp", Detail = "(property) iMover.moveUp: () => void" }, new LSP.CompletionItem { Label = "watch", Detail = "(property) iBaseScope.watch: () => void" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
