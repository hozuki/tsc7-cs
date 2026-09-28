// ported from: internal/fourslash/tests/autoImportCrossProject_symlinks_toSrc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCrossProject_symlinks_toSrcCase
{
    public static void TestAutoImportCrossProject_symlinks_toSrc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/packages/app/package.json\n{ \"name\": \"app\", \"dependencies\": { \"dep\": \"*\" } }\n// @Filename: /home/src/workspaces/project/packages/app/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"lib\": [\"es5\"],\n    \"module\": \"commonjs\",\n    \"outDir\": \"dist\",\n    \"rootDir\": \"src\",\n    \"baseUrl\": \".\"\n  }\n  \"references\": [{ \"path\": \"../dep\" }]\n}\n// @Filename: /home/src/workspaces/project/packages/app/src/index.ts\ndep/**/\n// @Filename: /home/src/workspaces/project/packages/dep/package.json\n{ \"name\": \"dep\", \"main\": \"dist/index.js\", \"types\": \"dist/index.d.ts\" }\n// @Filename: /home/src/workspaces/project/packages/dep/tsconfig.json\n{\n  \"compilerOptions\": { \"lib\": [\"es5\"], \"outDir\": \"dist\", \"rootDir\": \"src\", \"module\": \"commonjs\" }\n}\n// @Filename: /home/src/workspaces/project/packages/dep/src/index.ts\nimport \"./sub/folder\";\n// @Filename: /home/src/workspaces/project/packages/dep/src/sub/folder/index.ts\nexport const dep = 0;\n// @link: /home/src/workspaces/project/packages/dep -> /home/src/workspaces/project/packages/app/node_modules/dep"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { AutoImportEntrypointDirectorySearch = TRI.TSTrue });
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { dep } from \"dep/src/sub/folder\";\n\ndep"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
