// ported from: internal/fourslash/tests/renameImportOfExportEquals2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameImportOfExportEquals2Case
{
    public static void TestRenameImportOfExportEquals2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|declare namespace /*N*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}N|] {\n    export var x: number;\n}|]\ndeclare module \"mod\" {\n    [|export = [|{| \"contextRangeIndex\": 2 |}N|];|]\n}\ndeclare module \"a\" {\n    [|import * as /*O*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 4 |}O|] from \"mod\";|]\n    [|export { [|{| \"contextRangeIndex\": 6 |}O|] as /*P*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}P|] };|] // Renaming N here would rename\n}\ndeclare module \"b\" {\n    [|import { [|{| \"contextRangeIndex\": 9 |}P|] as /*Q*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 9 |}Q|] } from \"a\";|]\n    export const y: typeof [|Q|].x;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "N", "O", "P", "Q");
                f.VerifyBaselineRenameAtRangesWithText(t, null!, "N", "O", "P", "Q");
            }
            finally
            {
                done();
            }
        });
    }

}
