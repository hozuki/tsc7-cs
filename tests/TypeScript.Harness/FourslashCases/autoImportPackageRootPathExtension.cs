// ported from: internal/fourslash/tests/autoImportPackageRootPathExtension_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPackageRootPathExtensionCase
{
    public static void TestAutoImportPackageRootPathExtension(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: /node_modules/pkg/package.json\n{\n    \"name\": \"pkg\",\n    \"version\": \"1.0.0\",\n    \"main\": \"lib\"\n }\n// @Filename: /node_modules/pkg/lib/index.d.mts\nexport declare function foo(): any;\n// @Filename: /package.json\n{\n    \"dependencies\": {\n       \"pkg\": \"*\"\n    }\n }\n// @Filename: /index.ts\nfoo/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { AutoImportEntrypointDirectorySearch = TRI.TSTrue });
                f.VerifyImportFixModuleSpecifiers(t, "", ["pkg/lib/index.mjs"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
