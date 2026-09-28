// ported from: internal/fourslash/tests/tripleSlashReferenceResolutionMode_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TripleSlashReferenceResolutionModeCase
{
    public static void TestTripleSlashReferenceResolutionMode(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n { \"compilerOptions\": { \"lib\": [\"es5\"], \"module\": \"nodenext\", \"declaration\": true, \"strict\": true, \"outDir\": \"out\" }, \"files\": [\"./index.ts\"] }\n// @Filename: /home/src/workspaces/project/package.json\n { \"private\": true, \"type\": \"commonjs\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/package.json\n{ \"name\": \"pkg\", \"version\": \"0.0.1\", \"exports\": { \"require\": \"./require.cjs\", \"default\": \"./import.js\" }, \"type\": \"module\" }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/require.d.cts\nexport {};\nexport interface PkgRequireInterface { member: any; }\ndeclare global { const pkgRequireGlobal: PkgRequireInterface; }\n// @Filename: /home/src/workspaces/project/node_modules/pkg/import.d.ts\nexport {};\nexport interface PkgImportInterface { field: any; }\ndeclare global { const pkgImportGlobal: PkgImportInterface; }\n// @Filename: /home/src/workspaces/project/index.ts\n/// <reference types=\"pkg\" resolution-mode=\"import\" />\npkgImportGlobal;\nexport {};"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "/home/src/workspaces/project/index.ts");
                f.VerifyNumberOfErrorsInCurrentFile(t, 0);
            }
            finally
            {
                done();
            }
        });
    }

}
