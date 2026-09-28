// ported from: internal/fourslash/tests/autoImportExportEqualsOfImportStar_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportExportEqualsOfImportStarCase
{
    public static void TestAutoImportExportEqualsOfImportStar(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: /node_modules/mdx/package.json\n{ \"name\": \"mdx\", \"version\": \"1.0.0\", \"types\": \"index.d.ts\" }\n// @Filename: /node_modules/mdx/index.d.ts\nimport * as mdx from './lib/index.js'\n\nexport = mdx\n// @Filename: /node_modules/mdx/lib/index.d.ts\nexport * from './core.js'\nexport * from './compile.js'\n// @Filename: /node_modules/mdx/lib/core.d.ts\nexport declare function core(): void\n// @Filename: /node_modules/mdx/lib/compile.d.ts\nexport declare function compile(): void\n// @Filename: /package.json\n{ \"dependencies\": { \"mdx\": \"*\" } }\n// @Filename: /index.ts\nmdx/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
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
