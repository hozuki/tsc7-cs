// ported from: internal/fourslash/tests/autoImportAllowImportingTsExtensionsPackageJsonImports2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportAllowImportingTsExtensionsPackageJsonImports2Case
{
    public static void TestAutoImportAllowImportingTsExtensionsPackageJsonImports2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"allowImportingTsExtensions\": true,\n    \"rootDir\": \"src\",\n    \"outDir\": \"dist\",\n    \"declarationDir\": \"types\",\n    \"declaration\": true\n  }\n}\n// @Filename: /package.json\n{\n  \"name\": \"self\",\n  \"type\": \"module\",\n  \"imports\": {\n    \"#*\": {\n      \"types\": \"./types/*\",\n      \"default\": \"./dist/*\"\n    }\n  }\n}\n// @Filename: /src/add.ts\nexport function add(a: number, b: number) {}\n// @Filename: /src/index.ts\nadd/*imports*/;\nexternal/*exports*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "imports", ["#add.js"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
