// ported from: internal/fourslash/tests/importNameCodeFix_barrelExport2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_barrelExport2Case
{
    public static void TestImportNameCodeFix_barrelExport2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @baseUrl: /\n// @Filename: /proj/foo/a.ts\nexport const A = 0;\n// @Filename: /proj/foo/b.ts\nexport {};\nA/*sibling*/\n// @Filename: /proj/foo/index.ts\nexport * from \"./a\";\nexport * from \"./b\";\n// @Filename: /proj/index.ts\nexport * from \"./foo\";\nexport * from \"./src\";\n// @Filename: /proj/src/a.ts\nexport {};\nA/*parent*/\n// @Filename: /proj/src/utils.ts\nexport function util() { return \"util\"; }\nexport { A } from \"../foo/a\";\n// @Filename: /proj/src/index.ts\nexport * from \"./a\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "sibling", ["proj/foo/a", "proj/src/utils", "proj", "proj/foo"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative") });
                f.VerifyImportFixModuleSpecifiers(t, "parent", ["proj/foo", "proj/foo/a", "proj/src/utils", "proj"], new LSUP.UserPreferences { ImportModuleSpecifierPreference = (MS.ImportModuleSpecifierPreference)("non-relative") });
            }
            finally
            {
                done();
            }
        });
    }

}
