// ported from: internal/fourslash/tests/renameLocationsForFunctionExpression01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameLocationsForFunctionExpression01Case
{
    public static void TestRenameLocationsForFunctionExpression01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = [|function [|{| \"contextRangeIndex\": 0 |}f|](g: any, h: any) {\n    [|f|]([|f|], g);\n}|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "f");
            }
            finally
            {
                done();
            }
        });
    }

}
