// ported from: internal/fourslash/tests/autoImportCrossProject_paths_sharedOutDir_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCrossProject_paths_sharedOutDirCase
{
    public static void TestAutoImportCrossProject_paths_sharedOutDir(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.base.json\n{\n  \"compilerOptions\": {\n    \"lib\": [\"es5\"],\n    \"module\": \"commonjs\",\n    \"baseUrl\": \".\",\n    \"paths\": {\n      \"packages/*\": [\"./packages/*\"]\n    }\n  }\n}\n// @Filename: /home/src/workspaces/project/packages/app/tsconfig.json\n{\n  \"extends\": \"../../tsconfig.base.json\",\n  \"compilerOptions\": { \"outDir\": \"../../dist/packages/app\" },\n  \"references\": [{ \"path\": \"../dep\" }]\n}\n// @Filename: /home/src/workspaces/project/packages/app/index.ts\ndep/**/\n// @Filename: /home/src/workspaces/project/packages/app/utils.ts\nimport \"packages/dep\";\n// @Filename: /home/src/workspaces/project/packages/dep/tsconfig.json\n{\n  \"extends\": \"../../tsconfig.base.json\",\n  \"compilerOptions\": { \"outDir\": \"../../dist/packages/dep\" }\n}\n// @Filename: /home/src/workspaces/project/packages/dep/index.ts\nimport \"./sub/folder\";\n// @Filename: /home/src/workspaces/project/packages/dep/sub/folder/index.ts\nexport const dep = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { dep } from \"packages/dep/sub/folder\";\n\ndep"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
