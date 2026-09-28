// ported from: internal/fourslash/tests/renameNamedImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RenameNamedImportCase
{
    public static void TestRenameNamedImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/lib/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/lib/index.ts\nconst unrelatedLocalVariable = 123;\nexport const someExportedVariable = unrelatedLocalVariable;\n// @Filename: /home/src/workspaces/project/src/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/src/index.ts\nimport { /*i*/someExportedVariable } from '../lib/index';\nsomeExportedVariable;\n// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"] } }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "/home/src/workspaces/project/lib/index.ts");
                f.GoToFile(t, "/home/src/workspaces/project/src/index.ts");
                f.VerifyBaselineRename(t, new LSUP.UserPreferences { UseAliasesForRename = TRI.TSTrue }, "i");
            }
            finally
            {
                done();
            }
        });
    }

}
