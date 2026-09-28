// ported from: internal/fourslash/tests/basicInterfaceMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class BasicInterfaceMembersCase
{
    public static void TestBasicInterfaceMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("export {};\ninterface Point {\n\tx: number;\n\ty: number;\n}\ndeclare const p: Point;\np./*a*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "x", Kind = LSP.CompletionItemKind.Field, SortText = LS.SortTextLocationPriority }, "y"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
