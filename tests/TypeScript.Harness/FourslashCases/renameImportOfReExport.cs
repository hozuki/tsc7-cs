// ported from: internal/fourslash/tests/renameImportOfReExport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameImportOfReExportCase
{
    public static void TestRenameImportOfReExport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noLib: true\ndeclare module \"a\" {\n    [|export class /*1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}C|] {}|]\n}\ndeclare module \"b\" {\n    [|export { /*2*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}C|] } from \"a\";|]\n}\ndeclare module \"c\" {\n    [|import { /*3*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 4 |}C|] } from \"b\";|]\n    export function f(c: [|C|]): void;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[3]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[5], f.Ranges()[6]);
            }
            finally
            {
                done();
            }
        });
    }

}
