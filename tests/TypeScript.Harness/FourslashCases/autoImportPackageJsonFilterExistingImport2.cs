// ported from: internal/fourslash/tests/autoImportPackageJsonFilterExistingImport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPackageJsonFilterExistingImport2Case
{
    public static void TestAutoImportPackageJsonFilterExistingImport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @module: preserve\n// @Filename: /home/src/workspaces/project/node_modules/@types/react/index.d.ts\nexport declare function useMemo(): void;\nexport declare function useState(): void;\n// @Filename: /home/src/workspaces/project/package.json\n{}\n// @Filename: /home/src/workspaces/project/index.ts\nuseMemo/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, [], null!);
                f.GoToBOF(t);
                f.InsertLine(t, "import { useState } from \"react\";");
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { useMemo, useState } from \"react\";\nuseMemo"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
