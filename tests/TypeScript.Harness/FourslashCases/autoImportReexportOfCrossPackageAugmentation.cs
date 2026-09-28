// ported from: internal/fourslash/tests/autoImportReexportOfCrossPackageAugmentation_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSU = TypeScript.LsUtil.LsUtil;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportReexportOfCrossPackageAugmentationCase
{
    public static void TestAutoImportReexportOfCrossPackageAugmentation(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/vitest/package.json\n{ \"name\": \"vitest\", \"version\": \"1.0.0\", \"types\": \"index.d.ts\" }\n// @Filename: /node_modules/vitest/index.d.ts\nexport { AugmentedInterface, uniqueFunction } from \"@vitest/expect\";\n// @Filename: /node_modules/vitest/augmentation.d.ts\nexport {};\ndeclare module \"@vitest/expect\" {\n    interface AugmentedInterface {\n        bar: string;\n    }\n\t\tfunction uniqueFunction(): void;\n}\n// @Filename: /node_modules/@vitest/expect/package.json\n{ \"name\": \"@vitest/expect\", \"version\": \"1.0.0\", \"types\": \"index.d.ts\" }\n// @Filename: /node_modules/@vitest/expect/index.d.ts\nexport interface AugmentedInterface {\n    baz: number;\n}\n// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"module\": \"commonjs\", \"strict\": true } }\n// @Filename: /package.json\n{ \"name\": \"test\", \"dependencies\": { \"vitest\": \"*\" } }\n// @Filename: /index.ts\nuniqueFunction/**/"u8);
            var prefs = LSU.NewDefaultUserPreferences();
            prefs!.AutoImportEntrypointDirectorySearch = TRI.TSTrue;
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.Configure(t, prefs);
                f.GoToMarker(t, "");
                f.BaselineAutoImportsCompletions(t, [""]);
            }
            finally
            {
                done();
            }
        });
    }

}
