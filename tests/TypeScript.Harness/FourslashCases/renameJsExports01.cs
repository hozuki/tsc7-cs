// ported from: internal/fourslash/tests/renameJsExports01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameJsExports01Case
{
    public static void TestRenameJsExports01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\n[|exports.[|{| \"contextRangeIndex\": 0 |}area|] = function (r) { return r * r; }|]\n// @Filename: b.js\nvar mod = require('./a');\nvar t = mod./*1*/[|area|](10);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1");
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "area");
            }
            finally
            {
                done();
            }
        });
    }

}
