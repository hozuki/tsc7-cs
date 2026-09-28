// ported from: internal/fourslash/tests/findAllRefsReExportRightNameWrongSymbol_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsReExportRightNameWrongSymbolCase
{
    public static void TestFindAllRefsReExportRightNameWrongSymbol(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n[|export const /*a*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}x|] = 0;|]\n// @Filename: /b.ts\n[|export const /*b*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}x|] = 0;|]\n//@Filename: /c.ts\n[|export { /*cFromB*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 4 |}x|] } from \"./b\";|]\n[|import { /*cFromA*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}x|] } from \"./a\";|]\n/*cUse*/[|x|];\n// @Filename: /d.ts\n[|import { /*d*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 9 |}x|] } from \"./c\";|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "a", "b", "cFromB", "cFromA", "cUse", "d");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[7], f.Ranges()[8]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[3]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[5]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[10]);
            }
            finally
            {
                done();
            }
        });
    }

}
