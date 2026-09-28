// ported from: internal/fourslash/tests/importNameCodeFix_pathsWithoutBaseUrl2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_pathsWithoutBaseUrl2Case
{
    public static void TestImportNameCodeFix_pathsWithoutBaseUrl2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /packages/test-package-1/tsconfig.json\n{\n  \"compilerOptions\": {\n    \"module\": \"commonjs\",\n    \"paths\": {\n      \"test-package-2/*\": [\"../test-package-2/src/*\"]\n    }\n  }\n}\n// @Filename: /packages/test-package-1/src/common/logging.ts\nexport class Logger {};\n// @Filename: /packages/test-package-1/src/something/index.ts\nLogger/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "");
                f.VerifyImportFixAtPosition(t, ["import { Logger } from \"../common/logging\";\n\nLogger"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
