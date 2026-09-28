// ported from: internal/fourslash/tests/importNameCodeFixInferEndingPreference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixInferEndingPreferenceCase
{
    public static void TestImportNameCodeFixInferEndingPreference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @moduleResolution: bundler\n// @Filename: /a.mts\nexport {};\n// @Filename: /b.ts\nexport {};\n// @Filename: /c.ts\nexport const c = 0;\n// @Filename: /main.ts\nimport {} from \"./a.mjs\";\nimport {} from \"./b\";\n\nc/**/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["./c"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
