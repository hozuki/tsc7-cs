// ported from: internal/fourslash/tests/completionListInObjectLiteral2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInObjectLiteral2Case
{
    public static void TestCompletionListInObjectLiteral2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface TelemetryService {\n    publicLog(eventName: string, data: any): any;\n};\nclass SearchResult {\n    count() { return 5; }\n    isEmpty() { return true; }\n    fileCount(): string { return \"\"; }\n}\nclass Foo {\n    public telemetryService: TelemetryService;   // If telemetry service is of type 'any' (i.e. uncomment below line), the drop-down list works\n    public telemetryService2;\n    private test() {\n        var onComplete = (searchResult: SearchResult) => {\n            var hasResults = !searchResult.isEmpty();  // Drop-down list on searchResult fine here\n            // No drop-down list available on searchResult members within object literal below\n            this.telemetryService.publicLog('searchResultsShown', { count: searchResult./*1*/count(), fileCount: searchResult.fileCount() });\n            this.telemetryService2.publicLog('searchResultsShown', { count: searchResult./*2*/count(), fileCount: searchResult.fileCount() });\n        };\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["count", "fileCount", "isEmpty"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
