// ported from: internal/fourslash/tests/importNameCodeFixNewImportNodeModules4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportNodeModules4Case
{
    public static void TestImportNameCodeFixNewImportNodeModules4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|f1/*0*/('');|]\n// @Filename: package.json\n{ \"dependencies\": { \"package-name\": \"latest\" } }\n// @Filename: node_modules/package-name/bin/lib/libfile.d.ts\nexport function f1(text: string): string;\n// @Filename: node_modules/package-name/bin/lib/libfile.js\nfunction f1(text) { }\nexports.f1 = f1;\n// @Filename: node_modules/package-name/package.json\n{\n  \"main\": \"bin/lib/libfile.js\",\n  \"types\": \"bin/lib/libfile.d.ts\"\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { f1 } from \"package-name\";\n\nf1('');"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
