// ported from: internal/fourslash/tests/completionListInArrowFunctionInUnclosedCallSite01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInArrowFunctionInUnclosedCallSite01Case
{
    public static void TestCompletionListInArrowFunctionInUnclosedCallSite01(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare function foo(...params: any[]): any;\nfunction getAllFiles(rootFileNames: string[]) {\n    var processedFiles = rootFileNames.map(fileName => foo(/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["fileName", "rootFileNames", "getAllFiles", "foo"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
