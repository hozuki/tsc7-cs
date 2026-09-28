// ported from: internal/fourslash/tests/autoImportSymlinkCaseSensitive_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportSymlinkCaseSensitiveCase
{
    public static void TestAutoImportSymlinkCaseSensitive(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\" } }\n// @Filename: /node_modules/.pnpm/mobx@6.0.4/node_modules/MobX/Foo.d.ts\nexport declare function autorun(): void;\n// @Filename: /index.ts\nautorun/**/\n// @Filename: /utils.ts\nimport \"MobX/Foo\";\n// @link: /node_modules/.pnpm/mobx@6.0.4/node_modules/MobX -> /node_modules/MobX\n// @link: /node_modules/.pnpm/mobx@6.0.4/node_modules/MobX -> /node_modules/.pnpm/cool-mobx-dependent@1.2.3/node_modules/MobX"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, new LSUP.UserPreferences { AutoImportEntrypointDirectorySearch = TRI.TSTrue });
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { autorun } from \"MobX/Foo\";\n\nautorun"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
