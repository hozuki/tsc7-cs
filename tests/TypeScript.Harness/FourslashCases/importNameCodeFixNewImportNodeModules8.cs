// ported from: internal/fourslash/tests/importNameCodeFixNewImportNodeModules8_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFixNewImportNodeModules8Case
{
    public static void TestImportNameCodeFixNewImportNodeModules8(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("[|f1/*0*/('');|]\n// @Filename: package.json\n{ \"dependencies\": { \"@scope/package-name\": \"latest\" } }\n// @Filename: node_modules/@scope/package-name/bin/lib/index.d.ts\nexport function f1(text: string): string;\n// @Filename: node_modules/@scope/package-name/bin/lib/index.js\nfunction f1(text) { }\nexports.f1 = f1;\n// @Filename: node_modules/@scope/package-name/package.json\n{\n  \"main\": \"bin/lib/index.js\",\n  \"types\": \"bin/lib/index.d.ts\"\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixAtPosition(t, ["import { f1 } from \"@scope/package-name\";\n\nf1('');"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
