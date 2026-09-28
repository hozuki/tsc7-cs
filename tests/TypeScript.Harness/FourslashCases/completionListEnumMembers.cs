// ported from: internal/fourslash/tests/completionListEnumMembers_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListEnumMembersCase
{
    public static void TestCompletionListEnumMembers(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("enum Foo {\n    bar,\n    baz\n}\n\nvar v = Foo./*valueReference*/ba;\nvar t :Foo./*typeReference*/ba;\nFoo.bar./*enumValueReference*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["valueReference", "typeReference"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["bar", "baz"] } });
                f.VerifyCompletions(t, "enumValueReference", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["toString", "toFixed", "toExponential", "toPrecision", "valueOf", "toLocaleString"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
