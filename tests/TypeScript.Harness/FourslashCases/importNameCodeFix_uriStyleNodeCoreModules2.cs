// ported from: internal/fourslash/tests/importNameCodeFix_uriStyleNodeCoreModules2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_uriStyleNodeCoreModules2Case
{
    public static void TestImportNameCodeFix_uriStyleNodeCoreModules2(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: /node_modules/@types/node/index.d.ts\ndeclare module \"fs\" { function writeFile(): void }\ndeclare module \"fs/promises\" { function writeFile(): Promise<void> }\ndeclare module \"node:fs\" { export * from \"fs\"; }\ndeclare module \"node:fs/promises\" { export * from \"fs/promises\"; }\n// @Filename: /other.ts\nimport \"node:fs/promises\";\n// @Filename: /index.ts\nwriteFile/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["node:fs", "node:fs/promises"], null!);
                f.GoToFile(t, "/other.ts");
                f.ReplaceLine(t, 0, "\n");
                f.GoToFile(t, "/index.ts");
                f.VerifyImportFixModuleSpecifiers(t, "", ["fs", "fs/promises", "node:fs", "node:fs/promises"], null!);
                f.GoToFile(t, "/other.ts");
                f.ReplaceLine(t, 0, "import \"node:fs/promises\";\n");
                f.GoToFile(t, "/index.ts");
                f.VerifyImportFixModuleSpecifiers(t, "", ["node:fs", "node:fs/promises"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
