// ported from: internal/fourslash/tests/getOutliningSpansForRegionsNoSingleLineFolds_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansForRegionsNoSingleLineFoldsCase
{
    public static void TestGetOutliningSpansForRegionsNoSingleLineFolds(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n[|//#region\nfunction foo()[| {\n\n}|]\n[|//these\n//should|]\n//#endregion not you|]\n[|// be\n// together|]\n\n[|//#region bla bla bla\n\nfunction bar()[| { }|]\n\n//#endregion|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyOutliningSpans(t);
            }
            finally
            {
                done();
            }
        });
    }

}
