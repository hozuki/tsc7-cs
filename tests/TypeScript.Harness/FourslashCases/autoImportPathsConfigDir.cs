// ported from: internal/fourslash/tests/autoImportPathsConfigDir_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPathsConfigDirCase
{
    public static void TestAutoImportPathsConfigDir(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n        \"paths\": {\n            \"@root/*\": [\"${configDir}/src/*\"]\n        }\n    }\n}\n// @Filename: src/one.ts\nexport const one = 1;\n// @Filename: src/foo/two.ts\none/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["@root/one"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
