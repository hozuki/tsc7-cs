// ported from: internal/fourslash/tests/findAllRefsOnImportAliases2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsOnImportAliases2Case
{
    public static void TestFindAllRefsOnImportAliases2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("//@Filename: a.ts\n[|export class /*class0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}Class|] {}|]\n//@Filename: b.ts\n[|import { /*class1*/[|{| \"contextRangeIndex\": 2 |}Class|] as /*c2_0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}C2|] } from \"./a\";|]\nvar c = new /*c2_1*/[|C2|]();\n//@Filename: c.ts\n[|export { /*class2*/[|{| \"contextRangeIndex\": 6 |}Class|] as /*c3*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}C3|] } from \"./a\";|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "class0", "class1", "class2", "c2_0", "c2_1", "c3");
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "Class", "C2", "C3");
            }
            finally
            {
                done();
            }
        });
    }

}
