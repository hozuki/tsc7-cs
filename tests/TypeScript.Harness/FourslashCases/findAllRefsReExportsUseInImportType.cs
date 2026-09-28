// ported from: internal/fourslash/tests/findAllRefsReExportsUseInImportType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsReExportsUseInImportTypeCase
{
    public static void TestFindAllRefsReExportsUseInImportType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /foo/types/types.ts\n[|export type /*full0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 0 |}Full|] = { prop: string; };|]\n// @Filename: /foo/types/index.ts\n[|import * as /*foo0*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 2 |}foo|] from './types';|]\n[|export { /*foo1*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 4 |}foo|] };|]\n// @Filename: /app.ts\n[|import { /*foo2*/[|{| \"isWriteAccess\": true, \"isDefinition\": true, \"contextRangeIndex\": 6 |}foo|] } from './foo/types';|]\nexport type fullType = /*foo3*/[|foo|]./*full1*/[|Full|];\ntype namespaceImport = typeof import('./foo/types');\ntype fullType2 = import('./foo/types')./*foo4*/[|foo|]./*full2*/[|Full|];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "full0", "full1", "full2", "foo0", "foo1", "foo2", "foo3", "foo4");
                f.VerifyBaselineRename(t, null!, f.Ranges()[1], f.Ranges()[9], f.Ranges()[11]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[3]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[5], f.Ranges()[10]);
                f.VerifyBaselineRename(t, null!, f.Ranges()[7], f.Ranges()[8]);
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSFalse }, f.Ranges()[7], f.Ranges()[8], f.Ranges()[10], f.Ranges()[3], f.Ranges()[5]);
            }
            finally
            {
                done();
            }
        });
    }

}
