// ported from: internal/fourslash/tests/completionListAtIdentifierDefinitionLocations_varDeclarations_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAtIdentifierDefinitionLocations_varDeclarationsCase
{
    public static void TestCompletionListAtIdentifierDefinitionLocations_varDeclarations(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var aa = 1;\nvar /*varName1*/\nvar a/*varName2*/\nvar a2,/*varName3*/\nvar a2, a/*varName4*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, f.Markers(), null!);
            }
            finally
            {
                done();
            }
        });
    }

}
