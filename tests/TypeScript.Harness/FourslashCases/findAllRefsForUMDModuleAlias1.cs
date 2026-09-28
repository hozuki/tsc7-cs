// ported from: internal/fourslash/tests/findAllRefsForUMDModuleAlias1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefsForUMDModuleAlias1Case
{
    public static void TestFindAllRefsForUMDModuleAlias1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: 0.d.ts\nexport function doThing(): string;\nexport function doTheOtherThing(): void;\n/*1*/export as namespace /*2*/myLib;\n// @Filename: 1.ts\n/// <reference path=\"0.d.ts\" />\n/*3*/myLib.doThing();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "1", "2", "3");
            }
            finally
            {
                done();
            }
        });
    }

}
