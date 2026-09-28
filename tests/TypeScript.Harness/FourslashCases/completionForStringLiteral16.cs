// ported from: internal/fourslash/tests/completionForStringLiteral16_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteral16Case
{
    public static void TestCompletionForStringLiteral16(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    a: string;\n    b: number;\n    c: string;\n}\n\ndeclare function f1<T>(key: keyof T): T;\ndeclare function f2<T>(a: keyof T, b: keyof T): T;\n\nf1<Foo>(\"/*1*/\",);\nf1<Foo>(\"/*2*/\");\nf1<Foo>(\"/*3*/\",,,);\nf2<Foo>(\"/*4*/\", \"/*5*/\",);\nf2<Foo>(\"/*6*/\", \"/*7*/\");\nf2<Foo>(\"/*8*/\", \"/*9*/\",,,);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a", "b", "c"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
