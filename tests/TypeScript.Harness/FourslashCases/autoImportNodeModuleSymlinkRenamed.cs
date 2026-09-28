// ported from: internal/fourslash/tests/autoImportNodeModuleSymlinkRenamed_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportNodeModuleSymlinkRenamedCase
{
    public static void TestAutoImportNodeModuleSymlinkRenamed(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/solution/package.json\n{\n    \"name\": \"monorepo\",\n    \"workspaces\": [\"packages/*\"]\n}\n// @Filename: /home/src/workspaces/solution/packages/utils/package.json\n{\n    \"name\": \"utils\",\n    \"version\": \"1.0.0\",\n    \"exports\": \"./dist/index.js\"\n}\n// @Filename: /home/src/workspaces/solution/packages/utils/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"lib\": [\"es5\"],\n        \"composite\": true,\n        \"module\": \"nodenext\",\n        \"rootDir\": \"src\",\n        \"outDir\": \"dist\"\n    },\n    \"include\": [\"src\"]\n}\n// @Filename: /home/src/workspaces/solution/packages/utils/src/index.ts\nexport function gainUtility() { return 0; }\n// @Filename: /home/src/workspaces/solution/packages/web/package.json\n{\n    \"name\": \"web\",\n    \"version\": \"1.0.0\",\n    \"dependencies\": {\n        \"@monorepo/utils\": \"file:../utils\"\n    }\n}\n// @Filename: /home/src/workspaces/solution/packages/web/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"lib\": [\"es5\"],\n        \"composite\": true,\n        \"module\": \"esnext\",\n        \"moduleResolution\": \"bundler\",\n        \"rootDir\": \"src\",\n        \"outDir\": \"dist\",\n        \"emitDeclarationOnly\": true\n    },\n    \"include\": [\"src\"],\n    \"references\": [\n        { \"path\": \"../utils\" }\n    ]\n}\n// @Filename: /home/src/workspaces/solution/packages/web/src/index.ts\ngainUtility/**/\n// @link: /home/src/workspaces/solution/packages/utils -> /home/src/workspaces/solution/node_modules/utils\n// @link: /home/src/workspaces/solution/packages/utils -> /home/src/workspaces/solution/node_modules/@monorepo/utils\n// @link: /home/src/workspaces/solution/packages/web -> /home/src/workspaces/solution/node_modules/web"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixModuleSpecifiers(t, "", ["@monorepo/utils"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
