// ported from: internal/fourslash/tests/importNameCodeFixNewImportBaseUrl0_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportBaseUrl0Case
{
    public static void TestImportNameCodeFixNewImportBaseUrl0(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|f1/*0*/();|]\n// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n        \"baseUrl\": \"./a\"\n    }\n}\n// @Filename: a/b.ts\nexport function f1() { };"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { f1 } from \"b\";\n\nf1();"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
