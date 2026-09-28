// ported from: internal/fourslash/tests/renameDefaultImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameDefaultImportCase
{
    public static void TestRenameDefaultImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: B.ts\n[|export default class /*1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}B|] {\n    test() {\n    }\n}|]\n// @Filename: A.ts\n[|import /*2*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}B|] from \"./B\";|]\nlet b = new [|B|]();\nb.test();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[3], f.Ranges()[4]);
                f.VerifyBaselineDocumentHighlights(t, null!, "1");
            }
            finally
            {
                done();
            }
        });
    }

}
