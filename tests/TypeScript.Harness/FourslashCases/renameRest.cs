// ported from: internal/fourslash/tests/renameRest_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameRestCase
{
    public static void TestRenameRest(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Gen {\n    x: number;\n    [|[|{| \"contextRangeIndex\": 0 |}parent|]: Gen;|]\n    millenial: string;\n}\nlet t: Gen;\nvar { x, ...rest } = t;\nrest.[|parent|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "parent");
            }
            finally
            {
                done();
            }
        });
    }

}
