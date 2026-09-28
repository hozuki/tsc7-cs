// ported from: internal/fourslash/tests/autoImportProvider_importsMap3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_importsMap3Case
{
    public static void TestAutoImportProvider_importsMap3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"nodenext\",\n    \"lib\": [\"es5\"],\n    \"rootDir\": \"src\",\n    \"outDir\": \"dist\"\n  }\n}\n// @Filename: /home/src/workspaces/project/package.json\n{\n  \"type\": \"module\",\n  \"imports\": {\n    \"#internal/\": \"./dist/internal/\"\n  }\n}\n// @Filename: /home/src/workspaces/project/src/internal/foo.ts\nexport function something(name: string) {}\n// @Filename: /home/src/workspaces/project/src/a.ts\nsomething/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyImportFixModuleSpecifiers(t, "", ["#internal/foo.js"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
