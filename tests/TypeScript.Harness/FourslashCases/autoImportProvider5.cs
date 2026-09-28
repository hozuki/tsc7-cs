// ported from: internal/fourslash/tests/autoImportProvider5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider5Case
{
    public static void TestAutoImportProvider5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @Filename: /home/src/workspaces/project/package.json\n{ \"dependencies\": { \"react-hook-form\": \"*\" } }\n// @Filename: /home/src/workspaces/project/node_modules/react-hook-form/package.json\n{ \"types\": \"dist/index.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/react-hook-form/dist/index.d.ts\nexport * from \"./useForm\";\n// @Filename: /home/src/workspaces/project/node_modules/react-hook-form/dist/useForm.d.ts\nexport declare function useForm(): void;\n// @Filename: /home/src/workspaces/project/index.ts\nuseForm/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { AutoImportEntrypointDirectorySearch = TRI.TSTrue });
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { useForm } from \"react-hook-form\";\n\nuseForm", "import { useForm } from \"react-hook-form/dist/useForm\";\n\nuseForm"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
