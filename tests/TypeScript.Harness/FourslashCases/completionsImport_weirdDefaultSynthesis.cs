// ported from: internal/fourslash/tests/completionsImport_weirdDefaultSynthesis_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsImport_weirdDefaultSynthesisCase
{
    public static void TestCompletionsImport_weirdDefaultSynthesis(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @esModuleInterop: false\n// @allowSyntheticDefaultImports: false\n// @Filename: /collection.ts\nclass Collection {\n  public static readonly default: typeof Collection = Collection;\n}\nexport = Collection as typeof Collection & { default: typeof Collection };\n// @Filename: /index.ts\nColle/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyApplyCodeActionFromCompletion(t, "", new F.ApplyCodeActionFromCompletionOptions { Name = "Collection", Source = "./collection", Description = "Add import from \"./collection\"", UserPreferences = null!, NewFileContent = "import Collection = require(\"./collection\");\n\nColle" });
            }
            finally
            {
                done();
            }
        });
    }

}
