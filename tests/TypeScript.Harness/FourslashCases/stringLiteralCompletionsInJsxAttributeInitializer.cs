// ported from: internal/fourslash/tests/stringLiteralCompletionsInJsxAttributeInitializer_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class StringLiteralCompletionsInJsxAttributeInitializerCase
{
    public static void TestStringLiteralCompletionsInJsxAttributeInitializer(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n// @filename: /a.tsx\ntype Props = { a: number } | { b: \"somethingelse\", c: 0 | 1 };\ndeclare function Foo(args: Props): any\n\nconst a1 = <Foo b={\"/*1*/\"} />\nconst a2 = <Foo b=\"/*2*/\" />\nconst a3 = <Foo b=\"somethingelse\"/*3*/ />\nconst a4 = <Foo b={\"somethingelse\"} /*4*/ />\nconst a5 = <Foo b={\"somethingelse\"} c={0} /*5*/ />"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["somethingelse"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["\"somethingelse\""] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["0", "1"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
