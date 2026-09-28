// ported from: internal/fourslash/tests/renameObjectSpread_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameObjectSpreadCase
{
    public static void TestRenameObjectSpread(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A1 { [|[|{| \"contextRangeIndex\": 0 |}a|]: number|] };\ninterface A2 { [|[|{| \"contextRangeIndex\": 2 |}a|]?: number|] };\nlet a1: A1;\nlet a2: A2;\nlet a12 = { ...a1, ...a2 };\na12.[|a|];"u8);
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
