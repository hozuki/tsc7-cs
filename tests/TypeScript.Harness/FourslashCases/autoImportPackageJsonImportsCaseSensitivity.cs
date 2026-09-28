// ported from: internal/fourslash/tests/autoImportPackageJsonImportsCaseSensitivity_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPackageJsonImportsCaseSensitivityCase
{
    public static void TestAutoImportPackageJsonImportsCaseSensitivity(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @allowImportingTsExtensions: true\n// @Filename: /package.json\n{\n  \"type\": \"module\",\n  \"imports\": {\n    \"#src/*\": \"./SRC/*\"\n  }\n}\n// @Filename: /src/add.ts\nexport function add(a: number, b: number) {}\n// @Filename: /src/index.ts\nadd/*imports*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "imports", ["#src/add.ts"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative") });
            }
            finally
            {
                done();
            }
        });
    }

}
