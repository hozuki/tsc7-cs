// ported from: internal/fourslash/tests/getOccurrencesTryCatchFinallyBroken_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesTryCatchFinallyBrokenCase
{
    public static void TestGetOccurrencesTryCatchFinallyBroken(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("t /*1*/ry {\n    t/*2*/ry {\n    }\n    ctch (x) {\n    }\n\n    tr {\n    }\n    fin/*3*/ally {\n    }\n}\nc/*4*/atch (e) {\n}\nf/*5*/inally {\n}\n\n// Missing catch variable\nt/*6*/ry {\n}\ncatc/*7*/h {\n}\n/*8*/finally {\n}\n\n// Missing try entirely\ncat/*9*/ch (x) {\n}\nfinal/*10*/ly {\n}"u8);
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
