// ported from: internal/fourslash/tests/autoImportPackageJsonImportsPreference2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPackageJsonImportsPreference2Case
{
    public static void TestAutoImportPackageJsonImportsPreference2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /package.json\n{\n  \"imports\": {\n    \"#*\": \"./src/*.ts\"\n  }\n}\n// @Filename: /src/a/b/c/something.ts\nexport function something(name: string): any;\n// @Filename: /a.ts\nsomething/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["./src/a/b/c/something"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("project-relative") });
            }
            finally
            {
                done();
            }
        });
    }

}
