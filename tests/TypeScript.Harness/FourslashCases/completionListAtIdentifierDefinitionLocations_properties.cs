// ported from: internal/fourslash/tests/completionListAtIdentifierDefinitionLocations_properties_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAtIdentifierDefinitionLocations_propertiesCase
{
    public static void TestCompletionListAtIdentifierDefinitionLocations_properties(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var aa = 1;\nclass A1 {\n    /*property1*/\n}\nclass A2 {\n    p/*property2*/\n}\nclass A3 {\n    public s/*property3*/\n}\nclass A4 {\n    a/*property4*/\n}\nclass A5 {\n    public a/*property5*/\n}\nclass A6 {\n    protected a/*property6*/\n}\nclass A7 {\n    private a/*property7*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionClassElementKeywords } });
            }
            finally
            {
                done();
            }
        });
    }

}
