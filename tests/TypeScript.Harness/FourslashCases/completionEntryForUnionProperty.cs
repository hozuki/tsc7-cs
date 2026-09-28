// ported from: internal/fourslash/tests/completionEntryForUnionProperty_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionEntryForUnionPropertyCase
{
    public static void TestCompletionEntryForUnionProperty(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface One {\n    commonProperty: number;\n    commonFunction(): number;\n}\n\ninterface Two {\n    commonProperty: string\n    commonFunction(): number;\n}\n\nvar x : One | Two;\n\nx./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "commonFunction", Detail = "(method) commonFunction(): number" }, new LSP.CompletionItem { Label = "commonProperty", Detail = "(property) commonProperty: string | number" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
