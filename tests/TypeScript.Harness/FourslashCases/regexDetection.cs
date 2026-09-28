// ported from: internal/fourslash/tests/regexDetection_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RegexDetectionCase
{
    public static void TestRegexDetection(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8(" /*1*/15 / /*2*/Math.min(61 / /*3*/42, 32 / 15) / /*4*/15;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.VerifyNotQuickInfoExists(t);
                f.GoToMarker(t, "2");
                f.VerifyQuickInfoIs(t, "var Math: Math", "An intrinsic object that provides basic mathematics functionality and constants.");
                f.GoToMarker(t, "3");
                f.VerifyNotQuickInfoExists(t);
                f.GoToMarker(t, "4");
                f.VerifyNotQuickInfoExists(t);
            }
            finally
            {
                done();
            }
        });
    }

}
