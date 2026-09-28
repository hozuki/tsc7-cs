// ported from: internal/fourslash/tests/autoImportRelativePathToMonorepoPackage_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportRelativePathToMonorepoPackageCase
{
    public static void TestAutoImportRelativePathToMonorepoPackage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"lib\": [\"es5\"]\n  }\n}\n// @Filename: /home/src/workspaces/project/packages/app/dist/index.d.ts\nimport {} from \"utils\";\nexport const app: number;\n// @Filename: /home/src/workspaces/project/packages/utils/package.json\n{ \"name\": \"utils\", \"version\": \"1.0.0\", \"main\": \"dist/index.js\" }\n// @Filename: /home/src/workspaces/project/packages/utils/dist/index.d.ts\nexport const x: number;\n// @link: /home/src/workspaces/project/packages/utils -> /home/src/workspaces/project/packages/app/node_modules/utils\n// @Filename: /home/src/workspaces/project/script.ts\nimport {} from \"./packages/app/dist/index.js\";\nx/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixModuleSpecifiers(t, "", ["./packages/utils/dist/index.js"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
