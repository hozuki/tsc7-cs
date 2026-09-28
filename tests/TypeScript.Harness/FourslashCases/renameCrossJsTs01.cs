// ported from: internal/fourslash/tests/renameCrossJsTs01_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameCrossJsTs01Case
{
    public static void TestRenameCrossJsTs01(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: a.js\n[|exports.[|{| \"contextRangeIndex\": 0 |}area|] = function (r) { return r * r; }|]\n// @Filename: b.ts\n[|import { [|{| \"contextRangeIndex\": 2 |}area|] } from './a';|]\nvar t = [|area|](10);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[3], f.Ranges()[4]);
            }
            finally
            {
                done();
            }
        });
    }

}
