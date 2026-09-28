// ported from: internal/fourslash/tests/importNameCodeFix_typesVersions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSUP = TypeScript.LsUtil;
using MS = TypeScript.ModuleSpecifiers;
using TRI = TypeScript.Core.Tristate;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportNameCodeFix_typesVersionsCase
{
    public static void TestImportNameCodeFix_typesVersions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @checkJs: true\n// @Filename: /node_modules/unified/package.json\n{\n  \"name\": \"unified\",\n  \"types\": \"types/ts3.444/index.d.ts\",\n  \"typesVersions\": {\n    \">=4.0\": {\n      \"types/ts3.444/*\": [\n        \"types/ts4.0/*\"\n      ]\n    }\n  }\n}\n// @Filename: /node_modules/unified/types/ts3.444/index.d.ts\nexport declare const x: number;\n// @Filename: /node_modules/unified/types/ts4.0/index.d.ts\nexport declare const x: number;\n// @Filename: /foo.js\nimport {} from \"unified\";\n// @Filename: /index.js\nx/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["unified", "unified/types/ts3.444/index.js"], new LSUP.UserPreferences { ImportModuleSpecifierEnding = (MS.ImportModuleSpecifierEndingPreference)("js"), AutoImportEntrypointDirectorySearch = TRI.TSTrue });
            }
            finally
            {
                done();
            }
        });
    }

}
