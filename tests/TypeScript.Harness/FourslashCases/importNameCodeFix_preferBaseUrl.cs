// ported from: internal/fourslash/tests/importNameCodeFix_preferBaseUrl_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_preferBaseUrlCase
{
    public static void TestImportNameCodeFix_preferBaseUrl(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{ \"compilerOptions\": { \"baseUrl\": \"./src\" } }\n// @Filename: /src/d0/d1/d2/file.ts\nfoo/**/;\n// @Filename: /src/d0/a.ts\nexport const foo = 0;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/src/d0/d1/d2/file.ts");
                f.VerifyImportFixAtPosition(t, ["import { foo } from \"d0/a\";\n\nfoo;"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
