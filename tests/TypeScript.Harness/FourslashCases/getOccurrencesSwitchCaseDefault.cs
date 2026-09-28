// ported from: internal/fourslash/tests/getOccurrencesSwitchCaseDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesSwitchCaseDefaultCase
{
    public static void TestGetOccurrencesSwitchCaseDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|switch|] (10) {\n    [|case|] 1:\n    [|case|] 2:\n    [|case|] 4:\n    [|case|] 8:\n        foo: switch (20) {\n            case 1:\n            case 2:\n                break;\n            default:\n                break foo;\n        }\n    [|case|] 0xBEEF:\n    [|default|]:\n        [|break|];\n    [|case|] 16:\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
            }
            finally
            {
                done();
            }
        });
    }

}
