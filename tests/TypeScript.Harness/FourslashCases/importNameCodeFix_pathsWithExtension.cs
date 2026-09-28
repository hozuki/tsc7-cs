// ported from: internal/fourslash/tests/importNameCodeFix_pathsWithExtension_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_pathsWithExtensionCase
{
    public static void TestImportNameCodeFix_pathsWithExtension(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"target\": \"ESNext\",\n    \"module\": \"Node16\",\n    \"moduleResolution\": \"Node16\",\n    \"rootDir\": \"./src\",\n    \"outDir\": \"./dist\",\n    \"paths\": {\n      \"#internals/*\": [\"./src/internals/*.ts\"]\n    }\n  },\n  \"include\": [\"src\"]\n}\n// @Filename: /src/internals/example.ts\nexport function helloWorld() {}\n// @Filename: /src/index.ts\nhelloWorld/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["#internals/example"], new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("js") });
            }
            finally
            {
                done();
            }
        });
    }

}
