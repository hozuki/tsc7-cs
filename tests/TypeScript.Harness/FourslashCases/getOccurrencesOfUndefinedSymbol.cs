// ported from: internal/fourslash/tests/getOccurrencesOfUndefinedSymbol_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesOfUndefinedSymbolCase
{
    public static void TestGetOccurrencesOfUndefinedSymbol(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var obj1: {\n    (bar: any): any;\n    new (bar: any): any;\n    [bar: any]: any;\n    bar: any;\n    foob(bar: any): any;\n};\n\nclass cls3 {\n    property zeFunc() {\n    super.ceFun/**/c();\n}\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, "");
            }
            finally
            {
                done();
            }
        });
    }

}
