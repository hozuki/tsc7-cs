// ported from: internal/fourslash/tests/completionEntryForDeferredMappedTypeMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionEntryForDeferredMappedTypeMembersCase
{
    public static void TestCompletionEntryForDeferredMappedTypeMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: test.ts\ninterface A { a: A }\ndeclare let a: A;\ntype Deep<T> = { [K in keyof T]: Deep<T[K]> }\ndeclare function foo<T>(deep: Deep<T>): T;\nconst out = foo(a);\nout./*1*/a\nout.a./*2*/a\nout.a.a./*3*/a"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
