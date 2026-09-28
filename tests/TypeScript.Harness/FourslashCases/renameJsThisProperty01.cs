// ported from: internal/fourslash/tests/renameJsThisProperty01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameJsThisProperty01Case
{
    public static void TestRenameJsThisProperty01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\nfunction bar() {\n    [|this.[|{| \"contextRangeIndex\": 0 |}x|] = 10;|]\n}\nvar t = new bar();\n[|t.[|{| \"contextRangeIndex\": 2 |}x|] = 11;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "x");
            }
            finally
            {
                done();
            }
        });
    }

}
