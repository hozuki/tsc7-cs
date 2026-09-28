// ported from: internal/fourslash/tests/tripleSlashRefPathCompletionContext_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TripleSlashRefPathCompletionContextCase
{
    public static void TestTripleSlashRefPathCompletionContext(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: f.ts\n/*f*/\n// @Filename: test.ts\n/// <reference path/*0*/=/*1*/\"/*8*/\n/// <reference path/*2*/=/*3*/\"/*9*/\"/*4*/ /*5*///*6*/>/*7*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["0", "1", "2", "3", "4", "5", "6", "7"] }, null!);
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["8", "9"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["f.ts"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
