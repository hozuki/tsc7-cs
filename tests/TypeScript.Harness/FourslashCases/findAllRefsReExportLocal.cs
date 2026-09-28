// ported from: internal/fourslash/tests/findAllRefsReExportLocal_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsReExportLocalCase
{
    public static void TestFindAllRefsReExportLocal(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\n// @strict: false\n// @Filename: /a.ts\n[|var /*ax0*/[|{| \"isDefinition\": true, \"contextRangeIndex\": 0 |}x|];|]\n[|export { /*ax1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}x|] };|]\n[|export { /*ax2*/[|{| \"contextRangeIndex\": 4 |}x|] as /*ay*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 4 |}y|] };|]\n// @Filename: /b.ts\n[|import { /*bx0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 7 |}x|], /*by0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 7 |}y|] } from \"./a\";|]\n/*bx1*/[|x|]; /*by1*/[|y|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "ax0", "ax1", "ax2", "bx0", "bx1", "ay", "by0", "by1");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[5]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[3]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[8], f.Ranges()[10]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[6]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[9], f.Ranges()[11]);
            }
            finally
            {
                done();
            }
        });
    }

}
