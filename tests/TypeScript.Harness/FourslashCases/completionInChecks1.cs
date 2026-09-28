// ported from: internal/fourslash/tests/completionInChecks1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionInChecks1Case
{
    public static void TestCompletionInChecks1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @target: esnext\ndeclare const obj: {\n  a?: string;\n  b: number;\n};\n\nif (\"/*1*/\" in obj) {}\nif ((((\"/*2*/\"))) in obj) {}\nif (\"/*3*/\" in (((obj)))) {}\nif ((((\"/*4*/\"))) in (((obj)))) {}\n\ntype MyUnion = { missing: true } | { result: string };\ndeclare const u: MyUnion;\nif (\"/*5*/\" in u) {}\n\nclass Cls1 { foo = ''; #bar = 0; }\ndeclare const c1: Cls1;\nif (\"/*6*/\" in c1) {}\n\nclass Cls2 { foo = ''; private bar = 0; }\ndeclare const c2: Cls2;\nif (\"/*7*/\" in c2) {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2", "3", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a", "b"] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["missing", "result"] } });
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["foo"] } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["bar", "foo"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
