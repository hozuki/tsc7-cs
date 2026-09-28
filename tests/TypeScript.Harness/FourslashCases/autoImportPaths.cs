// ported from: internal/fourslash/tests/autoImportPaths_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPathsCase
{
    public static void TestAutoImportPaths(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /package1/jsconfig.json\n{\n  \"compilerOptions\": {\n    checkJs: true,\n    \"paths\": {\n      \"package1/*\": [\"./*\"],\n      \"package2/*\": [\"../package2/*\"]\n    },\n    \"baseUrl\": \".\"\n  },\n  \"include\": [\n    \".\",\n    \"../package2\"\n  ]\n}\n// @Filename: /package1/file1.js\nbar/**/\n// @Filename: /package2/file1.js\nexport const bar = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["package2/file1"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("shortest") });
            }
            finally
            {
                done();
            }
        });
    }

}
