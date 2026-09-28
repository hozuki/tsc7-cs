// ported from: internal/fourslash/tests/incrementalJsDocAdjustsLengthsRight_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class IncrementalJsDocAdjustsLengthsRightCase
{
    public static void TestIncrementalJsDocAdjustsLengthsRight(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n\n/**\n * Pad `str` to `width`.\n *\n * @param {String} str\n * @param {Number} wid/*1*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.Insert(t, "th\n@");
            }
            finally
            {
                done();
            }
        });
    }

}
