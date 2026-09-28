// ported from: internal/fourslash/tests/autoImportAllowImportingTsExtensionsPackageJsonImports1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportAllowImportingTsExtensionsPackageJsonImports1Case
{
    public static void TestAutoImportAllowImportingTsExtensionsPackageJsonImports1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: node18\n// @allowImportingTsExtensions: true\n// @Filename: /node_modules/pkg/package.json\n{\n  \"name\": \"pkg\",\n  \"type\": \"module\",\n  \"exports\": {\n    \"./*\": {\n      \"types\": \"./types/*\",\n      \"default\": \"./dist/*\"\n    }\n  }\n}\n// @Filename: /node_modules/pkg/types/external.d.ts\nexport declare function external(name: string): any;\n// @Filename: /package.json\n{\n  \"name\": \"self\",\n  \"type\": \"module\",\n  \"imports\": {\n    \"#*\": \"./src/*\"\n  },\n  \"dependencies\": {\n    \"pkg\": \"*\"\n  }\n}\n// @Filename: /src/add.ts\nexport function add(a: number, b: number) {}\n// @Filename: /src/index.ts\nadd/*imports*/;\nexternal/*exports*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "imports", ["#add.ts"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "exports", ["pkg/external.js"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
