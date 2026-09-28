// ported from: internal/fourslash/tests/arityErrorAfterStringCompletions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ArityErrorAfterStringCompletionsCase
{
    public static void TestArityErrorAfterStringCompletions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = "// @strict: true\n\ninterface Events {\n  click: any;\n  drag: any;\n}\n\ndeclare function addListener<K extends keyof Events>(type: K, listener: (ev: Events[K]) => any): void;\n\n/*1*/addListener/*2*/(\"/*3*/\")";
            var (f, done) = F.Fourslash.NewFourslash(t, null, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["click", "drag"] } });
                f.VerifyErrorExistsBetweenMarkers(t, "1", "2");
            }
            finally
            {
                done();
            }
        });
    }

}
