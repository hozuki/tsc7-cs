// ported from: internal/fourslash/tests/genericTypeWithMultipleBases1MultiFile_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GenericTypeWithMultipleBases1MultiFileCase
{
    public static void TestGenericTypeWithMultipleBases1MultiFile(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: genericTypeWithMultipleBases_0.ts\ninterface iBaseScope {\n    watch: () => void;\n}\n// @Filename: genericTypeWithMultipleBases_1.ts\ninterface iMover {\n    moveUp: () => void;\n}\n// @Filename: genericTypeWithMultipleBases_2.ts\ninterface iScope<TModel> extends iBaseScope, iMover {\n    family: TModel;\n}\n// @Filename: genericTypeWithMultipleBases_3.ts\nvar x: iScope<number>;\n// @Filename: genericTypeWithMultipleBases_4.ts\nx./**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "watch", Detail = "(property) iBaseScope.watch: () => void" }, new LSP.CompletionItem { Label = "moveUp", Detail = "(property) iMover.moveUp: () => void" }, new LSP.CompletionItem { Label = "family", Detail = "(property) iScope<number>.family: number" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
