// ported from: internal/fourslash/tests/goToDefinitionImportMeta_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GoToDefinitionImportMetaCase
{
    public static void TestGoToDefinitionImportMeta(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @Filename: foo.ts\n/// <reference path='./bar.d.ts' />\nimport.me/*reference*/ta;\n//@Filename: bar.d.ts\ninterface ImportMeta {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineGoToDefinition(t, true, "reference");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
