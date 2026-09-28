// ported from: internal/fourslash/tests/autoImportSpecifierExcludeRegexes3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportSpecifierExcludeRegexes3Case
{
    public static void TestAutoImportSpecifierExcludeRegexes3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: preserve\n// @Filename: /node_modules/pkg/package.json\n{\n    \"name\": \"pkg\",\n    \"version\": \"1.0.0\",\n    \"exports\": {\n        \".\": \"./index.js\",\n        \"./utils\": \"./utils.js\"\n    }\n}\n// @Filename: /node_modules/pkg/utils.d.ts\nexport function add(a: number, b: number) {}\n// @Filename: /node_modules/pkg/index.d.ts\nexport * from \"./utils\";\n// @Filename: /src/index.ts\nadd/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["pkg", "pkg/utils"], null!);
                f.VerifyImportFixModuleSpecifiers(t, "", ["pkg/utils"], new LSUP.UserPreferences { AutoImportSpecifierExcludeRegexes = ["^pkg$"] });
            }
            finally
            {
                done();
            }
        });
    }

}
