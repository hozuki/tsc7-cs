// ported from: internal/fourslash/tests/quickInfoImportMeta_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoImportMetaCase
{
    public static void TestQuickInfoImportMeta(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @Filename: foo.ts\n/// <reference path='./bar.d.ts' />\nim/*1*/port.me/*2*/ta;\n//@Filename: bar.d.ts\n/**\n * The type of `import.meta`.\n *\n * If you need to declare that a given property exists on `import.meta`,\n * this type may be augmented via interface merging.\n */\n interface ImportMeta {\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
