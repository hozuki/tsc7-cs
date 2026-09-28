// ported from: internal/fourslash/tests/renameImportRequire_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameImportRequireCase
{
    public static void TestRenameImportRequire(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n[|import [|{| \"contextRangeIndex\": 0 |}e|] = require(\"mod4\");|]\n[|e|];\na = { [|e|] };\n[|export { [|{| \"contextRangeIndex\": 4 |}e|] };|]\n// @Filename: /b.ts\n[|import { [|{| \"contextRangeIndex\": 6 |}e|] } from \"./a\";|]\n[|export { [|{| \"contextRangeIndex\": 8 |}e|] };|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[2], f.Ranges()[3], f.Ranges()[5], f.Ranges()[7], f.Ranges()[9]);
            }
            finally
            {
                done();
            }
        });
    }

}
