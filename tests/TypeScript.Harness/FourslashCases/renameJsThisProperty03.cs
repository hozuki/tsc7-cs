// ported from: internal/fourslash/tests/renameJsThisProperty03_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameJsThisProperty03Case
{
    public static void TestRenameJsThisProperty03(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\nclass C {\n  constructor(y) {\n    [|this.[|{| \"contextRangeIndex\": 0 |}x|] = y;|]\n  }\n}\nvar t = new C(12);\n[|t.[|{| \"contextRangeIndex\": 2 |}x|] = 11;|]"u8);
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
