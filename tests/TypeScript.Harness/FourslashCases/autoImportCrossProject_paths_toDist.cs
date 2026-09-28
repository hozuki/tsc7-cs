// ported from: internal/fourslash/tests/autoImportCrossProject_paths_toDist_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCrossProject_paths_toDistCase
{
    public static void TestAutoImportCrossProject_paths_toDist(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/packages/app/package.json\n{ \"name\": \"app\", \"dependencies\": { \"dep\": \"*\" } }\n// @Filename: /home/src/workspaces/project/packages/app/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"lib\": [\"es5\"],\n    \"module\": \"commonjs\",\n    \"outDir\": \"dist\",\n    \"rootDir\": \"src\",\n    \"baseUrl\": \".\",\n    \"paths\": {\n      \"dep\": [\"../dep/src/main\"],\n      \"dep/dist/*\": [\"../dep/src/*\"]\n    }\n  }\n  \"references\": [{ \"path\": \"../dep\" }]\n}\n// @Filename: /home/src/workspaces/project/packages/app/src/index.ts\ndep1/*1*/;\n// @Filename: /home/src/workspaces/project/packages/app/src/utils.ts\ndep2/*2*/;\n// @Filename: /home/src/workspaces/project/packages/app/src/a.ts\nimport \"dep\";\n// @Filename: /home/src/workspaces/project/packages/dep/package.json\n{ \"name\": \"dep\", \"main\": \"dist/main.js\", \"types\": \"dist/main.d.ts\" }\n// @Filename: /home/src/workspaces/project/packages/dep/tsconfig.json\n{\n  \"compilerOptions\": { \"lib\": [\"es5\"], \"outDir\": \"dist\", \"rootDir\": \"src\", \"module\": \"commonjs\" }\n}\n// @Filename: /home/src/workspaces/project/packages/dep/src/main.ts\nimport \"./sub/folder\";\nexport const dep1 = 0;\n// @Filename: /home/src/workspaces/project/packages/dep/src/sub/folder/index.ts\nexport const dep2 = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "1");
                f.VerifyImportFixAtPosition(t, ["import { dep1 } from \"dep\";\n\ndep1;"], null!);
                f.GoToMarker(t, "2");
                f.VerifyImportFixAtPosition(t, ["import { dep2 } from \"dep/dist/sub/folder\";\n\ndep2;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
