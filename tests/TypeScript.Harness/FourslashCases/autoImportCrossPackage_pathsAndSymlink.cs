// ported from: internal/fourslash/tests/autoImportCrossPackage_pathsAndSymlink_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCrossPackage_pathsAndSymlinkCase
{
    public static void TestAutoImportCrossPackage_pathsAndSymlink(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/packages/common/package.json\n{\n  \"name\": \"@company/common\",\n  \"version\": \"1.0.0\",\n  \"main\": \"./lib/index.tsx\"\n}\n// @Filename: /home/src/workspaces/project/packages/common/lib/index.tsx\nexport function Tooltip {};\n// @Filename: /home/src/workspaces/project/packages/app/package.json\n{\n  \"name\": \"@company/app\",\n  \"version\": \"1.0.0\",\n  \"dependencies\": {\n    \"@company/common\": \"1.0.0\"\n  }\n}\n// @Filename: /home/src/workspaces/project/packages/app/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"composite\": true,\n    \"lib\": [\"es5\"],\n    \"module\": \"esnext\",\n    \"moduleResolution\": \"bundler\",\n    \"paths\": {\n      \"@/*\": [\"./*\"]\n    }\n  }\n}\n// @Filename: /home/src/workspaces/project/packages/app/lib/index.ts\nTooltip/**/\n// @link: /home/src/workspaces/project/packages/common -> /home/src/workspaces/project/node_modules/@company/common"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixModuleSpecifiers(t, "", ["@company/common"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
