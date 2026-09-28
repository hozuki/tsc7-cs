// ported from: internal/fourslash/tests/autoImportsCustomConditions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportsCustomConditionsCase
{
    public static void TestAutoImportsCustomConditions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @moduleResolution: bundler\n// @customConditions: custom\n// @Filename: /node_modules/dep/package.json\n{\n  \"name\": \"dep\",\n  \"version\": \"1.0.0\",\n  \"exports\": {\n    \".\": {\n      \"custom\": \"./dist/index.js\"\n    }\n  }\n}\n// @Filename: /node_modules/dep/dist/index.d.ts\nexport const dep: number;\n// @Filename: /index.ts\ndep/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["dep"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
