// ported from: internal/fourslash/tests/renameJsThisProperty05_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameJsThisProperty05Case
{
    public static void TestRenameJsThisProperty05(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\nclass C {\n  constructor(y) {\n    this.x = y;\n  }\n}\n[|C.prototype.[|{| \"contextRangeIndex\": 0 |}z|] = 1;|]\nvar t = new C(12);\n[|t.[|{| \"contextRangeIndex\": 2 |}z|] = 11;|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "z");
            }
            finally
            {
                done();
            }
        });
    }

}
