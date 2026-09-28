// ported from: internal/fourslash/tests/completionListAtNodeBoundary_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAtNodeBoundaryCase
{
    public static void TestCompletionListAtNodeBoundary(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Iterator<T, U> {\n    (value: T, index: any, list: any): U;\n}\n\ninterface WrappedArray<T> {\n    map<U>(iterator: Iterator<T, U>, context?: any): U[];\n}\n\ninterface Underscore {\n    <T>(list: T[]): WrappedArray<T>;\n    map<T, U>(list: T[], iterator: Iterator<T, U>, context?: any): U[];\n}\n\ndeclare var _: Underscore;\nvar a: string[];\nvar e = a.map(x => x./**/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["charAt"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
