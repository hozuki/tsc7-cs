// ported from: internal/fourslash/tests/codeFixAddConvertToUnknownForNonOverlappingTypes9_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixAddConvertToUnknownForNonOverlappingTypes9Case
{
    public static void TestCodeFixAddConvertToUnknownForNonOverlappingTypes9(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @allowJs: true\n// @filename: a.js\nlet x = /** @type {string} */ (100);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t, "Add 'unknown' conversion for non-overlapping types");
            }
            finally
            {
                done();
            }
        });
    }

}
