// ported from: internal/fourslash/tests/completionsDotDotDotInObjectLiteral1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsDotDotDotInObjectLiteral1Case
{
    public static void TestCompletionsDotDotDotInObjectLiteral1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// https://github.com/microsoft/TypeScript/issues/57540\n\nconst foo = { b: 100 };\n\nconst bar: {\n  a: number;\n  b: number;\n} = {\n  a: 42,\n  .../*1*/\n};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["foo"], Excludes = ["b"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
