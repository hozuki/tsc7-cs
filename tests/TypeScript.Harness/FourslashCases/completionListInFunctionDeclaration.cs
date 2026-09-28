// ported from: internal/fourslash/tests/completionListInFunctionDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInFunctionDeclarationCase
{
    public static void TestCompletionListInFunctionDeclaration(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nvar a = 0;\nfunction foo(/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", null!);
                f.Insert(t, "a");
                f.VerifyCompletions(t, null!, null!);
                f.Insert(t, " , ");
                f.VerifyCompletions(t, null!, null!);
                f.Insert(t, "b");
                f.VerifyCompletions(t, null!, null!);
                f.Insert(t, ":");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalTypes } });
                f.Insert(t, "number, ");
                f.VerifyCompletions(t, null!, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
