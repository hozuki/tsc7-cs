// ported from: internal/fourslash/tests/autoImportSymlinkedMonorepoProjectReferences_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportSymlinkedMonorepoProjectReferencesCase
{
    public static void TestAutoImportSymlinkedMonorepoProjectReferences(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /packages/project-b/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"composite\": true,\n    \"outDir\": \"./dist\",\n    \"rootDir\": \"./src\",\n    \"declaration\": true,\n    \"module\": \"commonjs\",\n    \"strict\": true\n  },\n  \"include\": [\"src\"]\n}\n// @Filename: /packages/project-b/package.json\n{\n  \"name\": \"project-b\",\n  \"version\": \"1.0.0\",\n  \"exports\": {\n    \".\": {\n      \"types\": \"./dist/index.d.ts\",\n      \"default\": \"./dist/index.js\"\n    }\n  }\n}\n// @Filename: /packages/project-b/src/index.ts\nexport const projectBValue: number = 42;\nexport function projectBFunction(): string { return \"hello\"; }\n// @Filename: /packages/project-b/dist/index.d.ts\nexport declare const projectBValue: number;\nexport declare function projectBFunction(): string;\n// @Filename: /packages/project-a/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"commonjs\",\n    \"strict\": true,\n    \"outDir\": \"./dist\",\n    \"rootDir\": \"./src\"\n  },\n  \"include\": [\"src\"],\n  \"references\": [{ \"path\": \"../project-b\" }]\n}\n// @Filename: /packages/project-a/package.json\n{ \"name\": \"project-a\", \"dependencies\": { \"project-b\": \"*\" } }\n// @Filename: /packages/project-a/src/index.ts\nimport { projectBValue } from \"project-b\";\nconsole.log(projectBValue);\nprojectBFunc/**/\n// @link: /packages/project-b -> /packages/project-a/node_modules/project-b"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

}
