// ported from: internal/fourslash/tests/autoImportPackageJsonImportsHashSlash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPackageJsonImportsHashSlashCase
{
    public static void TestAutoImportPackageJsonImportsHashSlashNodenext(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"rootDir\": \"./\",\n    \"outDir\": \"build\"\n  }\n}\n// @Filename: /package.json\n{\n  \"imports\": {\n    \"#/*\": {\n      \"types\": \"./src/*\",\n      \"default\": \"./src/*\"\n    }\n  }\n}\n// @Filename: /src/domain/entities/entity.ts\nexport const entity = 1;\n// @Filename: /src/features/deep/consumer.ts\nentit/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestAutoImportPackageJsonImportsHashSlashNode16(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"node16\"\n  }\n}\n// @Filename: /package.json\n{\n  \"imports\": {\n    \"#/*\": \"./src/*\"\n  }\n}\n// @Filename: /src/domain/entities/entity.ts\nexport const entity = 1;\n// @Filename: /src/consumer.ts\nentit/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

}
