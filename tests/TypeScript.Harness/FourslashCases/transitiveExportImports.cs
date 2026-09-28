// ported from: internal/fourslash/tests/transitiveExportImports_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TransitiveExportImportsCase
{
    public static void TestTransitiveExportImports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: a.ts\n[|class /*1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}A|] {\n}|]\n[|export = [|{| \"contextRangeIndex\": 2 |}A|];|]\n// @Filename: b.ts\n[|export import /*2*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 4 |}b|] = require('./a');|]\n// @Filename: c.ts\n[|import /*3*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}b|] = require('./b');|]\nvar a = new /*4*/[|b|]./**/[|b|]();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyQuickInfoExists(t);
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3", "4");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[3]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[5], f.Ranges()[9]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[7], f.Ranges()[8]);
            }
            finally
            {
                done();
            }
        });
    }

}
