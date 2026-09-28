// ported from: internal/fourslash/tests/autoImportProvider_pnpm_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportProvider_pnpmCase
{
    public static void TestAutoImportProvider_pnpm(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /home/src/workspaces/project/tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"lib\": [\"es5\"] } }\n// @Filename: /home/src/workspaces/project/package.json\n{ \"dependencies\": { \"mobx\": \"*\" } }\n// @Filename: /home/src/workspaces/project/node_modules/.pnpm/mobx@6.0.4/node_modules/mobx/package.json\n{ \"types\": \"dist/mobx.d.ts\" }\n// @Filename: /home/src/workspaces/project/node_modules/.pnpm/mobx@6.0.4/node_modules/mobx/dist/mobx.d.ts\nexport declare function autorun(): void;\n// @Filename: /home/src/workspaces/project/index.ts\nautorun/**/\n// @link: /home/src/workspaces/project/node_modules/.pnpm/mobx@6.0.4/node_modules/mobx -> /home/src/workspaces/project/node_modules/mobx"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { autorun } from \"mobx\";\n\nautorun"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
