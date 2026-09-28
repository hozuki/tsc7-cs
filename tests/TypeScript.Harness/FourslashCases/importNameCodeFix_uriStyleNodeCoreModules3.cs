// ported from: internal/fourslash/tests/importNameCodeFix_uriStyleNodeCoreModules3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_uriStyleNodeCoreModules3Case
{
    public static void TestImportNameCodeFix_uriStyleNodeCoreModules3(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: /node_modules/@types/node/index.d.ts\ndeclare module \"path\" { function join(...segments: readonly string[]): string; }\ndeclare module \"node:path\" { export * from \"path\"; }\ndeclare module \"fs\" { function writeFile(): void }\ndeclare module \"fs/promises\" { function writeFile(): Promise<void> }\ndeclare module \"node:fs\" { export * from \"fs\"; }\ndeclare module \"node:fs/promises\" { export * from \"fs/promises\"; }\n// @Filename: /other.ts\nimport \"node:fs/promises\";\n// @Filename: /noPrefix.ts\nimport \"path\";\nwriteFile/*noPrefix*/\n// @Filename: /prefix.ts\nimport \"node:path\";\nwriteFile/*prefix*/\n// @Filename: /mixed1.ts\nimport \"path\";\nimport \"node:path\";\nwriteFile/*mixed1*/\n// @Filename: /mixed2.ts\nimport \"node:path\";\nimport \"path\";\nwriteFile/*mixed2*/\n// @Filename: /test1.ts\nimport \"node:test\";\nimport \"path\";\nwriteFile/*test1*/\n// @Filename: /test2.ts\nimport \"node:test\";\nwriteFile/*test2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "noPrefix", ["fs", "fs/promises"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "prefix", ["node:fs", "node:fs/promises"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "mixed1", ["node:fs", "node:fs/promises"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "mixed2", ["node:fs", "node:fs/promises"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "test1", ["fs", "fs/promises"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "test2", ["node:fs", "node:fs/promises"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
