// ported from: internal/fourslash/tests/getOccurrencesSwitchCaseDefault5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesSwitchCaseDefault5Case
{
    public static void TestGetOccurrencesSwitchCaseDefault5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("switch/*1*/ (10) {\n    case/*2*/ 1:\n    case/*3*/ 2:\n    case/*4*/ 4:\n    case/*5*/ 8:\n        foo: switch/*6*/ (20) {\n            case/*7*/ 1:\n            case/*8*/ 2:\n                break/*9*/;\n            default/*10*/:\n                break foo;\n        }\n    case/*11*/ 0xBEEF:\n    default/*12*/:\n        break/*13*/;\n    case 16/*14*/:\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Markers()));
            }
            finally
            {
                done();
            }
        });
    }

}
