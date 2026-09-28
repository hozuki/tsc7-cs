// ported from: internal/fourslash/tests/importNameCodeFixNewImportPaths_withLeadingDotSlash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportPaths_withLeadingDotSlashCase
{
    public static void TestImportNameCodeFixNewImportPaths_withLeadingDotSlash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a.ts\n[|foo|]\n// @Filename: /thisHasPathMapping.ts\nexport function foo() {};\n// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \".\",\n        \"paths\": {\n            \"foo\": [\"././thisHasPathMapping\"]\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { foo } from \"foo\";\n\nfoo"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
