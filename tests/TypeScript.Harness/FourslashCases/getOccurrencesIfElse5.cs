// ported from: internal/fourslash/tests/getOccurrencesIfElse5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesIfElse5Case
{
    public static void TestGetOccurrencesIfElse5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("if/*1*/ (true) {\n    if/*2*/ (false) {\n    }\n    else/*3*/ {\n    }\n    if/*4*/ (true) {\n    }\n    else/*5*/ {\n        if/*6*/ (false)\n            if/*7*/ (true)\n                var x = undefined;\n    }\n}\nelse/*8*/            if (null) {\n}\nelse/*9*/ /* whar garbl */ if/*10*/ (undefined) {\n}\nelse/*11*/\nif/*12*/ (false) {\n}\nelse/*13*/ { }"u8);
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
