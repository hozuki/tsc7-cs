// ported from: internal/fourslash/tests/thisPredicateFunctionCompletions03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ThisPredicateFunctionCompletions03Case
{
    public static void TestThisPredicateFunctionCompletions03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class RoyalGuard {\n    isLeader(): this is LeadGuard {\n        return this instanceof LeadGuard;\n    }\n    isFollower(): this is FollowerGuard {\n        return this instanceof FollowerGuard;\n    }\n}\n\nclass LeadGuard extends RoyalGuard {\n    lead(): void {};\n}\n\nclass FollowerGuard extends RoyalGuard {\n    follow(): void {};\n}\n\nlet a: RoyalGuard = new FollowerGuard();\nif (a.is/*1*/Leader()) {\n    a./*2*/;\n}\nelse if (a.is/*3*/Follower()) {\n    a./*4*/;\n}\n\ninterface GuardInterface {\n   isLeader(): this is LeadGuard;\n   isFollower(): this is FollowerGuard;\n}\n\nlet b: GuardInterface;\nif (b.is/*5*/Leader()) {\n    b./*6*/;\n}\nelse if (b.is/*7*/Follower()) {\n    b./*8*/;\n}\n\nlet leader/*13*/Status = a.isLeader();\nfunction isLeaderGuard(g: RoyalGuard) {\n   return g.isLeader();\n}\nlet checked/*14*/LeaderStatus = isLeader/*15*/Guard(a);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "6"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["lead", "isLeader", "isFollower"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["4", "8"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["follow", "isLeader", "isFollower"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
