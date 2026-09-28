// ported from: internal/fourslash/tests/renameImportOfReExport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameImportOfReExport2Case
{
    public static void TestRenameImportOfReExport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare module \"a\" {\n    [|export class /*1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}C|] {}|]\n}\ndeclare module \"b\" {\n    [|export { [|{| \"contextRangeIndex\": 2 |}C|] as /*2*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}D|] } from \"a\";|]\n}\ndeclare module \"c\" {\n    [|import { /*3*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 5 |}D|] } from \"b\";|]\n    export function f(c: [|D|]): void;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
                f.VerifyBaselineRename(t, null!, U.ToAny(f.GetRangesByText().Get("C")!));
                f.VerifyBaselineRename(t, null!, f.GetRangesByText().Get("D")![0]);
                f.VerifyBaselineRename(t, null!, f.GetRangesByText().Get("D")![1], f.GetRangesByText().Get("D")![2]);
            }
            finally
            {
                done();
            }
        });
    }

}
