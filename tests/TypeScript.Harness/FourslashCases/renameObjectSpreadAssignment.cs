// ported from: internal/fourslash/tests/renameObjectSpreadAssignment_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameObjectSpreadAssignmentCase
{
    public static void TestRenameObjectSpreadAssignment(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface A1 { a: number };\ninterface A2 { a?: number };\n[|let [|{| \"contextRangeIndex\": 0 |}a1|]: A1;|]\n[|let [|{| \"contextRangeIndex\": 2 |}a2|]: A2;|]\nlet a12 = { ...[|a1|], ...[|a2|] };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[4], f.Ranges()[3], f.Ranges()[5]);
            }
            finally
            {
                done();
            }
        });
    }

}
