// ported from: internal/fourslash/tests/autoImportSymlinkedMonorepo_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportSymlinkedMonorepoCase
{
    public static void TestAutoImportSymlinkedMonorepo(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /packages/project-b/package.json\n{ \"name\": \"project-b\", \"version\": \"1.0.0\", \"main\": \"index.js\", \"types\": \"index.d.ts\" }\n// @Filename: /packages/project-b/index.d.ts\nexport declare const projectBValue: number;\nexport declare function projectBFunction(): string;\n// @Filename: /packages/project-a/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"strict\": true } }\n// @Filename: /packages/project-a/package.json\n{ \"name\": \"project-a\", \"dependencies\": { \"project-b\": \"*\" } }\n// @Filename: /packages/project-a/index.ts\nimport { projectBValue } from \"project-b\";\nconsole.log(projectBValue);\nprojectBFunc/**/\n// @link: /packages/project-b -> /packages/project-a/node_modules/project-b"u8);
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
