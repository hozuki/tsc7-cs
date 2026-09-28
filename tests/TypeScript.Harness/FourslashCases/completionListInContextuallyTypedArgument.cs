// ported from: internal/fourslash/tests/completionListInContextuallyTypedArgument_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInContextuallyTypedArgumentCase
{
    public static void TestCompletionListInContextuallyTypedArgument(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface MyPoint {\n    x1: number;\n    y1: number;\n}\n\nfunction foo(a: (e: MyPoint) => string) { }\nfoo((e) => {\n    e./*1*/\n} );\n\nclass test {\n    constructor(a: (e: MyPoint) => string) { }\n}\nvar t = new test((e) => {\n    e./*2*/\n} );"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["x1", "y1"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
