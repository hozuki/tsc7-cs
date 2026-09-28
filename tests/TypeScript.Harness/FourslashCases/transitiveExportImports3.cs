// ported from: internal/fourslash/tests/transitiveExportImports3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TransitiveExportImports3Case
{
    public static void TestTransitiveExportImports3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n[|export function /*f*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}f|]() {}|]\n// @Filename: b.ts\n[|export { [|{| \"contextRangeIndex\": 2 |}f|] as /*g0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}g|] } from \"./a\";|]\n[|import { /*f2*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 5 |}f|] } from \"./a\";|]\n[|import { /*g1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 7 |}g|] } from \"./b\";|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "f", "g0", "g1", "f2");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[3]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[6]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[4]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[8]);
            }
            finally
            {
                done();
            }
        });
    }

}
