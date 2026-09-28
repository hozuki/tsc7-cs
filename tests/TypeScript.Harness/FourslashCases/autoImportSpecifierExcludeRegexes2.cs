// ported from: internal/fourslash/tests/autoImportSpecifierExcludeRegexes2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportSpecifierExcludeRegexes2Case
{
    public static void TestAutoImportSpecifierExcludeRegexes2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"preserve\",\n        \"paths\": {\n            \"@app/*\": [\"./src/*\"]\n        }\n    }\n}\n// @Filename: /src/utils.ts\nexport function add(a: number, b: number) {}\n// @Filename: /src/index.ts\nadd/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["./utils"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "", ["@app/utils"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["^\\./"] });
                f.VerifyImportFixModuleSpecifiers(t, "", ["@app/utils"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative") });
                f.VerifyImportFixModuleSpecifiers(t, "", ["./utils"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative"), AutoImportSpecifierExcludeRegexes = ["^@app/"] });
                f.VerifyImportFixModuleSpecifiers(t, "", [], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["utils"] });
            }
            finally
            {
                done();
            }
        });
    }

}
