// ported from: internal/fourslash/tests/findAllRefsReExportStarAs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsReExportStarAsCase
{
    public static void TestFindAllRefsReExportStarAs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /leafModule.ts\nexport const /*helloDef*/hello = () => 'Hello';\n// @Filename: /exporting.ts\nexport * as /*leafDef*/Leaf from './leafModule';\n// @Filename: /importing.ts\n import { /*leafImportDef*/Leaf } from './exporting';\n /*leafUse*/[|Leaf|]./*helloUse*/[|hello|]()"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoErrors(t);
                f.VerifyBaselineFindAllReferences(t, "helloDef", "helloUse", "leafDef", "leafImportDef", "leafUse");
            }
            finally
            {
                done();
            }
        });
    }

}
