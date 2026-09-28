// ported from: internal/fourslash/tests/renameReExportDefault_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameReExportDefaultCase
{
    public static void TestRenameReExportDefault(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\nexport { default } from \"./b\";\n[|export { default as [|{| \"contextRangeIndex\": 0 |}b|] } from \"./b\";|]\nexport { default as bee } from \"./b\";\n[|import { default as [|{| \"contextRangeIndex\": 2 |}b|] } from \"./b\";|]\nimport { default as bee } from \"./b\";\n[|import [|{| \"contextRangeIndex\": 4 |}b|] from \"./b\";|]\n// @Filename: /b.ts\n[|const [|{| \"contextRangeIndex\": 6 |}b|] = 0;|]\n[|export default [|{| \"contextRangeIndex\": 8 |}b|];|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[3], f.Ranges()[5], f.Ranges()[7], f.Ranges()[9]);
            }
            finally
            {
                done();
            }
        });
    }

}
