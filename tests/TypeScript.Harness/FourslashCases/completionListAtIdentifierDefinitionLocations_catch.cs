// ported from: internal/fourslash/tests/completionListAtIdentifierDefinitionLocations_catch_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListAtIdentifierDefinitionLocations_catchCase
{
    public static void TestCompletionListAtIdentifierDefinitionLocations_catch(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var aa = 1;\n try {} catch(/*catchVariable1*/\n try {} catch(a/*catchVariable2*/"u8);
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
