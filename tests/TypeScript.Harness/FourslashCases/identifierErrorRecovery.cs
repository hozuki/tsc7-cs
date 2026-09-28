// ported from: internal/fourslash/tests/identifierErrorRecovery_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IdentifierErrorRecoveryCase
{
    public static void TestIdentifierErrorRecovery(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var /*1*/export/*2*/;\nvar foo;\nvar /*3*/class/*4*/;\nvar bar;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyErrorExistsBetweenMarkers(t, "1", "2");
                f.VerifyErrorExistsBetweenMarkers(t, "3", "4");
                f.VerifyNumberOfErrorsInCurrentFile(t, 3);
                f.GoToEOF(t);
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["foo", "bar"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
