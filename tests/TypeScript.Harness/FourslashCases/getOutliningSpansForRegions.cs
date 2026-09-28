// ported from: internal/fourslash/tests/getOutliningSpansForRegions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetOutliningSpansForRegionsCase
{
    public static void TestGetOutliningSpansForRegions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// region without label\n[|// #region\n\n// #endregion|]\n\n// region without label with trailing spaces\n[|// #region\n\n// #endregion|]\n\n// region with label\n[|// #region label1\n\n// #endregion|]\n\n// region with extra whitespace in all valid locations\n             [|//              #region          label2    label3\n\n        //        #endregion|]\n\n// No space before directive\n[|//#region label4\n\n//#endregion|]\n\n// Nested regions\n[|// #region outer\n\n[|// #region inner\n\n// #endregion inner|]\n\n// #endregion outer|]\n\n// region delimiters not valid when there is preceding text on line\n test // #region invalid1\n\ntest // #endregion\n\n// region delimiters not valid when in multiline comment\n/*\n// #region invalid2\n*/\n\n/*\n// #endregion\n*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyOutliningSpans(t, LSP.FoldingRangeKind.Region);
            }
            finally
            {
                done();
            }
        });
    }

}
