// ported from: internal/fourslash/tests/completionListInUnclosedFunction19_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInUnclosedFunction19Case
{
    public static void TestCompletionListInUnclosedFunction19(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface MyType {\n}\n\nfunction foo(x: string, y: number, z: boolean) {\n    function bar(a: number, b: string = \"hello\", c: typeof x = \"hello\") {\n        var v = (p: MyType) => { return y + /*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["foo", "x", "y", "z", "bar", "a", "b", "c", "v", "p"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
