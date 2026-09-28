// ported from: internal/fourslash/tests/importNameCodeFixNewImportPaths1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportPaths1Case
{
    public static void TestImportNameCodeFixNewImportPaths1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|foo/*0*/();|]\n// @Filename: folder_b/f2.ts\nexport function foo() {};\n// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \".\",\n        \"paths\": {\n            \"b/*\": [ \"folder_b/*\" ]\n        }\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { foo } from \"b/f2\";\n\nfoo();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
