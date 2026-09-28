// ported from: internal/fourslash/tests/completionsInArrayLiteralWithContextualType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsInArrayLiteralWithContextualTypeCase
{
    public static void TestCompletionsInArrayLiteralWithContextualType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content1 = FourslashCaseContent.U8("let y: [\"foo\" | \"bar\", string] = [/*a*/];"u8);
            var content2 = FourslashCaseContent.U8("let z: [\"a\", \"b\" | \"c\"] = [\"a\", /*b*/];"u8);
            var content3 = FourslashCaseContent.U8("let x: { \"-1\": \"hello\" } = [/*c*/];"u8);
            var content4 = FourslashCaseContent.U8("let x: [number] = [123]/*d*/;"u8);
            var (f1, done1) = F.Fourslash.NewFourslash(t, null!, content1);
            try
            {
                f1.VerifyCompletions(t, "a", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["\"foo\"", "\"bar\""] } });
            }
            finally
            {
                done1();
            }
            var (f2, done2) = F.Fourslash.NewFourslash(t, null!, content2);
            try
            {
                f2.VerifyCompletions(t, "b", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["\"b\"", "\"c\""] } });
            }
            finally
            {
                done2();
            }
            var (f3, done3) = F.Fourslash.NewFourslash(t, null!, content3);
            try
            {
                f3.VerifyCompletions(t, "c", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Excludes = ["\"hello\""] } });
            }
            finally
            {
                done3();
            }
            var (f4, done4) = F.Fourslash.NewFourslash(t, null!, content4);
            try
            {
                f4.VerifyCompletions(t, "d", new F.CompletionsExpectedList { ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems {  } });
            }
            finally
            {
                done4();
            }
        });
    }

}
