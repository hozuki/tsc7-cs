// ported from: internal/fourslash/tests/autoImportCrossProject_baseUrl_toDist_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportCrossProject_baseUrl_toDistCase
{
    public static void TestAutoImportCrossProject_baseUrl_toDist(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/common/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"lib\": [\"es5\"],\n    \"module\": \"commonjs\",\n    \"outDir\": \"dist\",\n    \"composite\": true\n  },\n  \"include\": [\"src\"]\n}\n// @Filename: /home/src/workspaces/project/common/src/MyModule.ts\nexport function square(n: number) {\n  return n * 2;\n}\n// @Filename: /home/src/workspaces/project/web/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"lib\": [\"es5\"],\n    \"module\": \"esnext\",\n    \"moduleResolution\": \"node\",\n    \"noEmit\": true,\n    \"baseUrl\": \".\"\n  },\n  \"include\": [\"src\"],\n  \"references\": [{ \"path\": \"../common\" }]\n}\n// @Filename: /home/src/workspaces/project/web/src/MyApp.ts\nimport { square } from \"../../common/dist/src/MyModule\";\n// @Filename: /home/src/workspaces/project/web/src/Helper.ts\nexport function saveMe() {\n  square/**/(2);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "/home/src/workspaces/project/web/src/Helper.ts");
                f.VerifyImportFixModuleSpecifiers(t, "", ["../../common/src/MyModule"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative") });
            }
            finally
            {
                done();
            }
        });
    }

}
