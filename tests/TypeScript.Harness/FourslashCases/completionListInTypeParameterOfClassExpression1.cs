// ported from: internal/fourslash/tests/completionListInTypeParameterOfClassExpression1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInTypeParameterOfClassExpression1Case
{
    public static void TestCompletionListInTypeParameterOfClassExpression1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nvar C0 = class D</*0*/\nvar C1 = class D</*1*/T> {}\nvar C2 = class D<T, /*2*/\nvar C3 = class D<T, /*3*/U>{}\nvar C4 = class D<T extends /*4*/>{}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["0", "1", "2", "3"] }, null!);
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionGlobalTypesPlus(["D"]) } });
            }
            finally
            {
                done();
            }
        });
    }

}
