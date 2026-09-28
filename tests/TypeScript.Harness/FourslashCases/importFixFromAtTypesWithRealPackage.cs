// ported from: internal/fourslash/tests/importFixFromAtTypesWithRealPackage_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ImportFixFromAtTypesWithRealPackageCase
{
    public static void TestImportFixFromAtTypesWithRealPackage(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/myLib/package.json\n{\"name\":\"myLib\",\"version\":\"1.0.0\",\"main\":\"index.js\"}\n// @Filename: /node_modules/myLib/index.js\nmodule.exports = {};\n// @Filename: /node_modules/@types/myLib/package.json\n{\"name\":\"@types/myLib\",\"version\":\"1.0.0\",\"types\":\"index.d.ts\"}\n// @Filename: /node_modules/@types/myLib/index.d.ts\nexport function f1(): void;\nexport function f2(): void;\n// @Filename: /package.json\n{\"dependencies\":{\"myLib\":\"*\"}}\n// @Filename: /other.ts\nimport { f1 } from \"myLib\";\nf1();\n// @Filename: /index.ts\n[|f2/*0*/();|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "0", ["myLib"], null!);
            }
            finally
            {
                done();
            }
        });
    }

    public static void TestImportFixFromAtTypesWithRealPackageExports(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/myLib/package.json\n{\"name\":\"myLib\",\"version\":\"1.0.0\",\"exports\":{\".\":{\"default\":\"./index.js\"}}}\n// @Filename: /node_modules/myLib/index.js\nmodule.exports = {};\n// @Filename: /node_modules/@types/myLib/package.json\n{\"name\":\"@types/myLib\",\"version\":\"1.0.0\",\"types\":\"index.d.ts\"}\n// @Filename: /node_modules/@types/myLib/index.d.ts\nexport function f1(): void;\nexport function f2(): void;\n// @Filename: /package.json\n{\"dependencies\":{\"myLib\":\"*\"}}\n// @Filename: /other.ts\nimport { f1 } from \"myLib\";\nf1();\n// @Filename: /index.ts\n[|f2/*0*/();|]"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "0", ["myLib"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
