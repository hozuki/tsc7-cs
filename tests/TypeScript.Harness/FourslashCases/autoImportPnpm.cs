// ported from: internal/fourslash/tests/autoImportPnpm_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPnpmCase
{
    public static void TestAutoImportPnpm(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\" } }\n// @Filename: /node_modules/.pnpm/mobx@6.0.4/node_modules/mobx/package.json\n{ \"types\": \"dist/mobx.d.ts\" }\n// @Filename: /node_modules/.pnpm/mobx@6.0.4/node_modules/mobx/dist/mobx.d.ts\nexport declare function autorun(): void;\n// @Filename: /index.ts\nautorun/**/\n// @Filename: /utils.ts\nimport \"mobx\";\n// @link: /node_modules/.pnpm/mobx@6.0.4/node_modules/mobx -> /node_modules/mobx\n// @link: /node_modules/.pnpm/mobx@6.0.4/node_modules/mobx -> /node_modules/.pnpm/cool-mobx-dependent@1.2.3/node_modules/mobx"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
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
