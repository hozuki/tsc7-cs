// ported from: internal/fourslash/tests/transitiveExportImports2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TransitiveExportImports2Case
{
    public static void TestTransitiveExportImports2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: a.ts\n[|namespace /*A*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}A|] {\n    export const x = 0;\n}|]\n// @Filename: b.ts\n[|export import /*B*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}B|] = [|A|];|]\n[|B|].x;\n// @Filename: c.ts\n[|import { /*C*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}B|] } from \"./b\";|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "A", "B", "C");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[4]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[3], f.Ranges()[5]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[7]);
            }
            finally
            {
                done();
            }
        });
    }

}
