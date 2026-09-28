// ported from: internal/fourslash/tests/autoImportBundlerBlockRelativeNodeModulesPaths_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportBundlerBlockRelativeNodeModulesPathsCase
{
    public static void TestAutoImportBundlerBlockRelativeNodeModulesPaths(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: esnext\n// @moduleResolution: bundler\n// @Filename: /node_modules/dep/package.json\n{\n  \"name\": \"dep\",\n  \"version\": \"1.0.0\",\n  \"exports\": \"./dist/index.js\"\n}\n// @Filename: /node_modules/dep/dist/utils.d.ts\nexport const util: () => void;\n// @Filename: /node_modules/dep/dist/index.d.ts\nexport * from \"./utils\";\n// @Filename: /index.ts\nutil/**/"u8);
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
