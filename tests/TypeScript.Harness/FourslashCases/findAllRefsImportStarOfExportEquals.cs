// ported from: internal/fourslash/tests/findAllRefsImportStarOfExportEquals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsImportStarOfExportEqualsCase
{
    public static void TestFindAllRefsImportStarOfExportEquals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowSyntheticDefaultimports: true\n// @Filename: /node_modules/a/index.d.ts\n[|declare function /*a0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}a|](): void;|]\n[|declare namespace /*a1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}a|] {\n    export const x: number;\n}|]\n[|export = /*a2*/[|{| \"contextRangeIndex\": 4 |}a|];|]\n// @Filename: /b.ts\n[|import /*b0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}b|] from \"a\";|]\n/*b1*/[|b|]();\n[|b|].x;\n// @Filename: /c.ts\n[|import /*c0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 10 |}a|] from \"a\";|]\n/*c1*/[|a|]();\n/*c2*/[|a|].x;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "a0", "a1", "a2", "b0", "b1", "c0", "c1", "c2");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[3], f.Ranges()[5]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[7], f.Ranges()[8], f.Ranges()[9]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[11], f.Ranges()[12], f.Ranges()[13]);
            }
            finally
            {
                done();
            }
        });
    }

}
