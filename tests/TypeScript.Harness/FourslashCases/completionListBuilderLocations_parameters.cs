// ported from: internal/fourslash/tests/completionListBuilderLocations_parameters_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListBuilderLocations_parametersCase
{
    public static void TestCompletionListBuilderLocations_parameters(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var aa = 1;\nclass bar1{ constructor(/*1*/\nclass bar2{ constructor(a/*2*/\nclass bar3{ constructor(a, /*3*/\nclass bar4{ constructor(a, b/*4*/\nclass bar6{ constructor(public a, /*5*/\nclass bar7{ constructor(private a, /*6*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionConstructorParameterKeywords } });
            }
            finally
            {
                done();
            }
        });
    }

}
