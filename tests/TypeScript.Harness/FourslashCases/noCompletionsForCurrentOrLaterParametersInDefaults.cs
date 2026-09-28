// ported from: internal/fourslash/tests/noCompletionsForCurrentOrLaterParametersInDefaults_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class NoCompletionsForCurrentOrLaterParametersInDefaultsCase
{
    public static void TestNoCompletionsForCurrentOrLaterParametersInDefaults(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f1(a = /*1*/, b) { }\nfunction f2(a = a/*2*/, b) { }\nfunction f3(a = a + /*3*/, b = a/*4*/, c = /*5*/) { }\nfunction f3(a) {\n    function f4(b = /*6*/, c) { }\n}\nconst f5 = (a, b = (c = /*7*/, e) => { }, d = b) => { }\n\ntype A1<K = /*T1*/, L> = K"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["a", "b"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["a", "b"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["6"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a"], Excludes = ["b", "c"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["7"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b", "d"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["T1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["K", "L"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
