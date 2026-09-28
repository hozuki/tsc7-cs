// ported from: internal/fourslash/tests/completionsInMapConstructorNoCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsInMapConstructorNoCrashCase
{
    public static void TestCompletionsInMapConstructorNoCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content1 = FourslashCaseContent.U8("const m = new Map([\n    [/*a*/'0', ['0', false]],\n]);"u8);
            var content2 = FourslashCaseContent.U8("const m = new Map([\n    ['0', ['0', false]]/*b*/,\n]);"u8);
            var (f1, done1) = F.Fourslash.NewFourslash(t, null!, content1);
            try
            {
                f1.VerifyCompletions(t, "a", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done1();
            }
            var (f2, done2) = F.Fourslash.NewFourslash(t, null!, content2);
            try
            {
                f2.VerifyCompletions(t, "b", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done2();
            }
        });
    }

}
