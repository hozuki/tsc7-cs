// ported from: internal/fourslash/tests/completionListInReturnWithContextualThis_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInReturnWithContextualThisCase
{
    public static void TestCompletionListInReturnWithContextualThis(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Ctx {\n    foo(): {\n        x: number\n    };\n}\n\ndeclare function wrap(cb: (this: Ctx) => any): void;\n\nwrap(function () {\n    const xs = this.foo();\n    return xs./*inReturn*/\n});\n\nwrap(function () {\n    const xs = this.foo();\n    const y = xs./*involvedInReturn*/\n    return y;\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "inReturn", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["x"] } });
                f.VerifyCompletions(t, "involvedInReturn", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["x"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
