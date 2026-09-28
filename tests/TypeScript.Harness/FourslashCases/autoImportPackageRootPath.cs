// ported from: internal/fourslash/tests/autoImportPackageRootPath_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPackageRootPathCase
{
    public static void TestAutoImportPackageRootPath(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /node_modules/pkg/package.json\n{\n    \"name\": \"pkg\",\n    \"version\": \"1.0.0\",\n    \"main\": \"lib\",\n    \"module\": \"lib\"\n }\n// @Filename: /node_modules/pkg/lib/index.js\nexport function foo() {};\n// @Filename: /package.json\n{\n    \"dependencies\": {\n       \"pkg\": \"*\"\n    }\n }\n// @Filename: /index.ts\nfoo/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["pkg"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
