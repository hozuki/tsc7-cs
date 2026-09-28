// ported from: internal/fourslash/tests/renameImportOfExportEquals_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameImportOfExportEqualsCase
{
    public static void TestRenameImportOfExportEquals(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|declare namespace /*N*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}N|] {\n    [|export var /*x*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}x|]: number;|]\n}|]\ndeclare module \"mod\" {\n    [|export = [|{| \"contextRangeIndex\": 4 |}N|];|]\n}\ndeclare module \"a\" {\n    [|import * as /*a*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}N|] from \"mod\";|]\n    [|export { [|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 8 |}N|] };|] // Renaming N here would rename\n}\ndeclare module \"b\" {\n    [|import { /*b*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 10 |}N|] } from \"a\";|]\n    export const y: typeof [|N|].[|x|];\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "N", "a", "b", "x");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[5]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[7]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[9]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[11], f.Ranges()[12]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[3], f.Ranges()[13]);
            }
            finally
            {
                done();
            }
        });
    }

}
