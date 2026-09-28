// ported from: internal/fourslash/tests/getOccurrencesReturnBroken_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesReturnBrokenCase
{
    public static void TestGetOccurrencesReturnBroken(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("ret/*1*/urn;\nretu/*2*/rn;\nfunction f(a: number) {\n    if (a > 0) {\n        return (function () {\n            () => [|return|];\n            [|return|];\n            [|return|];\n\n            if (false) {\n                [|return|] true;\n            }\n        })() || true;\n    }\n\n    var unusued = [1, 2, 3, 4].map(x => { return 4 })\n\n    return;\n    return true;\n}\n\nclass A {\n    ret/*3*/urn;\n    r/*4*/eturn 8675309;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Ranges()));
                f.VerifyBaselineDocumentHighlights(t, null!, U.ToAny(f.Markers()));
            }
            finally
            {
                done();
            }
        });
    }

}
