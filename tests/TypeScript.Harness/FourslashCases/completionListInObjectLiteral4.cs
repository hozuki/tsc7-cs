// ported from: internal/fourslash/tests/completionListInObjectLiteral4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInObjectLiteral4Case
{
    public static void TestCompletionListInObjectLiteral4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strictNullChecks: true\ninterface Thing {\n    hello: number;\n    world: string;\n}\n\ndeclare function funcA(x : Thing): void;\ndeclare function funcB(x?: Thing): void;\ndeclare function funcC(x : Thing | null): void;\ndeclare function funcD(x : Thing | undefined): void;\ndeclare function funcE(x : Thing | null | undefined): void;\ndeclare function funcF(x?: Thing | null | undefined): void;\n\nfuncA({ /*A*/ });\nfuncB({ /*B*/ });\nfuncC({ /*C*/ });\nfuncD({ /*D*/ });\nfuncE({ /*E*/ });\nfuncF({ /*F*/ });"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["hello", "world"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
