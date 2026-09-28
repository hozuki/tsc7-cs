// ported from: internal/fourslash/tests/importNameCodeFixNewImportPaths2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportPaths2Case
{
    public static void TestImportNameCodeFixNewImportPaths2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|foo/*0*/();|]\n// @Filename: folder_b/index.ts\nexport function foo() {};\n// @Filename: tsconfig.path.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \".\",\n        \"paths\": {\n            \"b\": [ \"folder_b/index\" ]\n        }\n    }\n}\n// @Filename: tsconfig.json\n{\n    \"extends\": \"./tsconfig.path\",\n    \"compilerOptions\": { }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { foo } from \"b\";\n\nfoo();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
