// ported from: internal/fourslash/tests/importNameCodeFix_barrelExport3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_barrelExport3Case
{
    public static void TestImportNameCodeFix_barrelExport3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @Filename: /foo/a.ts\nexport const A = 0;\n// @Filename: /foo/b.ts\nexport {};\nA/*sibling*/\n// @Filename: /foo/index.ts\nexport * from \"./a\";\nexport * from \"./b\";\n// @Filename: /index.ts\nexport * from \"./foo\";\nexport * from \"./src\";\n// @Filename: /src/a.ts\nexport {};\nA/*parent*/\n// @Filename: /src/index.ts\nexport * from \"./a\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "sibling", ["./a", "./index", "../index"], new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("index") });
                f.VerifyImportFixModuleSpecifiers(t, "parent", ["../foo/a", "../foo/index", "../index"], new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("index") });
            }
            finally
            {
                done();
            }
        });
    }

}
