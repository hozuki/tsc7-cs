// ported from: internal/fourslash/tests/quickInfoForUMDModuleAlias_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForUMDModuleAliasCase
{
    public static void TestQuickInfoForUMDModuleAlias(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: 0.d.ts\nexport function doThing(): string;\nexport function doTheOtherThing(): void;\nexport as namespace /*0*/myLib;\n// @Filename: 1.ts\n/// <reference path=\"0.d.ts\" />\n/*1*/myLib.doThing();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "0", "export namespace myLib", "");
                f.VerifyQuickInfoAt(t, "1", "export namespace myLib", "");
            }
            finally
            {
                done();
            }
        });
    }

}
