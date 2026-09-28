// ported from: internal/fourslash/tests/completionListWithoutVariableinitializer_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListWithoutVariableinitializerCase
{
    public static void TestCompletionListWithoutVariableinitializer(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("const a = a/*1*/;\nconst b = a && b/*2*/;\nconst c = [{ prop: [c/*3*/] }];\nconst d = () => { d/*4*/ };\nconst e = () => expression/*5*/\nconst f = { prop() { e/*6*/ }  };\nconst fn = (p = /*7*/) => {}\nconst { g, h = /*8*/ } = { ... }\nconst [ g1, h1 = /*9*/ ] = [ ... ]\nconst { a1 } = a/*10*/;\nconst { a2 } = fn({a: a/*11*/});\nconst [ a3 ] = a/*12*/;\nconst [ a4 ] = fn([a/*13*/]);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["a"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a"], Excludes = ["b"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b"], Excludes = ["c"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b", "c", "d"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b", "c", "d", "e"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["6"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b", "c", "d", "e"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["7"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b", "c", "d", "e", "fn"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["8"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b", "c", "d", "e", "fn"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["9"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", "b", "c", "d", "e", "fn"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["10"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["a1"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["11"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["a2"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["12"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["a3"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["13"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["a4"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
