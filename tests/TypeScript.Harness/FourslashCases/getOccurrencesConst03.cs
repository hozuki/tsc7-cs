// ported from: internal/fourslash/tests/getOccurrencesConst03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesConst03Case
{
    public static void TestGetOccurrencesConst03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("namespace m {\n    export /*1*/const x;\n    export [|const|] enum E {\n    }\n}\n\nexport /*2*/const x;\nexport [|const|] enum E {\n}"u8);
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
