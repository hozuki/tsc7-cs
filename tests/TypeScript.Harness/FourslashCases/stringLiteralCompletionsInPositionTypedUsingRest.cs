// ported from: internal/fourslash/tests/stringLiteralCompletionsInPositionTypedUsingRest_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StringLiteralCompletionsInPositionTypedUsingRestCase
{
    public static void TestStringLiteralCompletionsInPositionTypedUsingRest(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function pick<T extends object, K extends keyof T>(obj: T, ...keys: K[]): Pick<T, K>;\ndeclare function pick2<T extends object, K extends (keyof T)[]>(obj: T, ...keys: K): Pick<T, K[number]>;\n\nconst obj = { aaa: 1, bbb: '2', ccc: true };\n\npick(obj, 'aaa', '/*ts1*/');\npick2(obj, 'aaa', '/*ts2*/');\nclass Q<T> {\n  public select<Keys extends keyof T>(...args: Keys[]) {}\n}\nnew Q<{ id: string; name: string }>().select(\"name\", \"/*ts3*/\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts1", "ts2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["aaa", "bbb", "ccc"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["id", "name"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
