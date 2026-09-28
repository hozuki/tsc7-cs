// ported from: internal/fourslash/tests/autoImportCrossProjectNodeModules_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCrossProjectNodeModulesCase
{
    public static void TestAutoImportCrossProjectNodeModules(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/pkg-listed/package.json\n{ \"name\": \"pkg-listed\", \"version\": \"1.0.0\" }\n// @Filename: /node_modules/pkg-listed/index.d.ts\nexport declare const pkg_listed_value: number;\n// @Filename: /node_modules/pkg-unlisted/package.json\n{ \"name\": \"pkg-unlisted\", \"version\": \"1.0.0\" }\n// @Filename: /node_modules/pkg-unlisted/index.d.ts\nexport declare const pkg_unlisted_value: string;\n// @Filename: /project-a/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"strict\": true } }\n// @Filename: /project-a/package.json\n{ \"name\": \"project-a\", \"dependencies\": { \"pkg-listed\": \"*\" } }\n// @Filename: /project-a/index.ts\nimport { pkg_unlisted_value } from \"pkg-unlisted\";\nconsole.log(pkg_unlisted_value);\n// @Filename: /project-b/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"strict\": true } }\n// @Filename: /project-b/package.json\n{ \"name\": \"project-b\", \"dependencies\": { \"pkg-listed\": \"*\" } }\n// @Filename: /project-b/index.ts\npkg_/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/project-a/index.ts");
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
