// ported from: internal/fourslash/tests/getOccurrencesIfElse_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesIfElseCase
{
    public static void TestGetOccurrencesIfElse(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|if|] (true) {\n    if (false) {\n    }\n    else {\n    }\n    if (true) {\n    }\n    else {\n        if (false)\n            if (true)\n                var x = undefined;\n    }\n}\n[|else            i/**/f|] (null) {\n}\n[|else|] /* whar garbl */ [|if|] (undefined) {\n}\n[|else|]\n[|if|] (false) {\n}\n[|else|] { }"u8);
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
