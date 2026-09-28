// ported from: internal/fourslash/tests/quickInfoLink2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoLink2Case
{
    public static void TestQuickInfoLink2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @Filename: quickInfoLink2.js\n/**\n * @typedef AdditionalWallabyConfig/**/ Additional valid Wallaby config properties\n * that aren't defined in {@link IWallabyConfig}.\n * @property {boolean} autoDetect\n */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
