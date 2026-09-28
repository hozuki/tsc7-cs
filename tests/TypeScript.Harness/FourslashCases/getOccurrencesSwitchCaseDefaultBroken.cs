// ported from: internal/fourslash/tests/getOccurrencesSwitchCaseDefaultBroken_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesSwitchCaseDefaultBrokenCase
{
    public static void TestGetOccurrencesSwitchCaseDefaultBroken(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("swi/*1*/tch(10) {\n    case 1:\n    case 2:\n    c/*2*/ase 4:\n    case 8:\n    case 0xBEEF:\n    de/*4*/fult:\n        break;\n    /*5*/cas 16:\n    c/*3*/ase 12:\n        function f() {\n            br/*11*/eak;\n            /*12*/break;\n        }\n}\n\nsw/*6*/itch (10) {\n    de/*7*/fault\n    case 1:\n    case 2\n\n    c/*8*/ose 4:\n    case 8:\n    case 0xBEEF:\n        bre/*9*/ak;\n    case 16:\n        () => bre/*10*/ak;\n}"u8);
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
