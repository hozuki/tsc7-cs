// ported from: internal/fourslash/tests/getOccurrencesAfterEdit_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesAfterEditCase
{
    public static void TestGetOccurrencesAfterEdit(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/*0*/\ninterface A {\n    foo: string;\n}\nfunction foo(x: A) {\n    x.f/*1*/oo\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, "1");
                f.GoToMarker(t, "0");
                f.Insert(t, "\n");
                f.VerifyBaselineDocumentHighlights(t, null!, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
