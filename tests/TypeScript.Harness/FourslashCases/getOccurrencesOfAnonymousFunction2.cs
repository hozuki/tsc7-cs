// ported from: internal/fourslash/tests/getOccurrencesOfAnonymousFunction2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOccurrencesOfAnonymousFunction2Case
{
    public static void TestGetOccurrencesOfAnonymousFunction2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//global foo definition\nfunction foo() {}\n\n(function f/*local*/oo(): number {\n    return foo(); // local foo reference\n})\n//global foo references\nfo/*global*/o();\nvar f = foo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineDocumentHighlights(t, null!, "local", "global");
            }
            finally
            {
                done();
            }
        });
    }

}
