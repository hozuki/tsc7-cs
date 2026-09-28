// ported from: internal/fourslash/tests/getOccurrencesReturn2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesReturn2Case
{
    public static void TestGetOccurrencesReturn2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f(a: number) {\n    if (a > 0) {\n        return (function () {\n            [|return|];\n            [|ret/**/urn|];\n            [|return|];\n\n            while (false) {\n                [|return|] true;\n            }\n        })() || true;\n    }\n\n    var unusued = [1, 2, 3, 4].map(x => { return 4 })\n\n    return;\n    return true;\n}"u8);
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
