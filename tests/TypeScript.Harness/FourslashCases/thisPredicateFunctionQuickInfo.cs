// ported from: internal/fourslash/tests/thisPredicateFunctionQuickInfo_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ThisPredicateFunctionQuickInfoCase
{
    public static void TestThisPredicateFunctionQuickInfo(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class RoyalGuard {\n    isLeader(): this is LeadGuard {\n        return this instanceof LeadGuard;\n    }\n    isFollower(): this is FollowerGuard {\n        return this instanceof FollowerGuard;\n    }\n}\n\nclass LeadGuard extends RoyalGuard {\n    lead(): void {};\n}\n\nclass FollowerGuard extends RoyalGuard {\n    follow(): void {};\n}\n\nlet a: RoyalGuard = new FollowerGuard();\nif (a.is/*1*/Leader()) {\n    a./*2*/;\n}\nelse if (a.is/*3*/Follower()) {\n    a./*4*/;\n}\n\ninterface GuardInterface {\n   isLeader(): this is LeadGuard;\n   isFollower(): this is FollowerGuard;\n}\n\nlet b: GuardInterface;\nif (b.is/*5*/Leader()) {\n    b./*6*/;\n}\nelse if (b.is/*7*/Follower()) {\n    b./*8*/;\n}\n\nif (((a.isLeader)())) {\n    a./*9*/;\n}\nelse if (((a).isFollower())) {\n    a./*10*/;\n}\n\nif (((a[\"isLeader\"])())) {\n    a./*11*/;\n}\nelse if (((a)[\"isFollower\"]())) {\n    a./*12*/;\n}\n\nlet leader/*13*/Status = a.isLeader();\nfunction isLeaderGuard(g: RoyalGuard) {\n   return g.isLeader();\n}\nlet checked/*14*/LeaderStatus = isLeader/*15*/Guard(a);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(method) RoyalGuard.isLeader(): this is LeadGuard", "");
                f.VerifyQuickInfoAt(t, "3", "(method) RoyalGuard.isFollower(): this is FollowerGuard", "");
                f.VerifyQuickInfoAt(t, "5", "(method) GuardInterface.isLeader(): this is LeadGuard", "");
                f.VerifyQuickInfoAt(t, "7", "(method) GuardInterface.isFollower(): this is FollowerGuard", "");
                f.VerifyQuickInfoAt(t, "13", "let leaderStatus: boolean", "");
                f.VerifyQuickInfoAt(t, "14", "let checkedLeaderStatus: boolean", "");
                f.VerifyQuickInfoAt(t, "15", "function isLeaderGuard(g: RoyalGuard): g is LeadGuard", "");
            }
            finally
            {
                done();
            }
        });
    }

}
