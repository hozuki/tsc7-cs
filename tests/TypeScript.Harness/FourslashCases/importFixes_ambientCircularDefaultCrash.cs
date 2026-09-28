// ported from: internal/fourslash/tests/importFixes_ambientCircularDefaultCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportFixes_ambientCircularDefaultCrashCase
{
    public static void TestImportFixes_ambientCircularDefaultCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"preserve\",\n    \"lib\": [\"es5\"]\n  }\n}\n// @Filename: /home/src/workspaces/project/types.d.ts\ndeclare module \"mymod\" {\n  import mymod from \"mymod\";\n  export default mymod;\n}\n// @Filename: /home/src/workspaces/project/index.ts\nmy/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.VerifyImportFixModuleSpecifiers(t, "", [], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
