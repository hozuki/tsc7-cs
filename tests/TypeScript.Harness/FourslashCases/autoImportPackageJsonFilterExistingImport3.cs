// ported from: internal/fourslash/tests/autoImportPackageJsonFilterExistingImport3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPackageJsonFilterExistingImport3Case
{
    public static void TestAutoImportPackageJsonFilterExistingImport3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"], \"module\": \"preserve\", \"types\": [\"*\"] } }\n// @Filename: /home/src/workspaces/project/node_modules/@types/node/index.d.ts\ndeclare module \"node:fs\" {\n    export function readFile(): void;\n    export function writeFile(): void;\n}\n// @Filename: /home/src/workspaces/project/package.json\n{}\n// @Filename: /home/src/workspaces/project/index.ts\nreadFile/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, [], null!);
                f.GoToBOF(t);
                f.InsertLine(t, "import { writeFile } from \"node:fs\";");
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { readFile, writeFile } from \"node:fs\";\nreadFile"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
