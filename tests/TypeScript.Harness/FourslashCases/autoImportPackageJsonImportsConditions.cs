// ported from: internal/fourslash/tests/autoImportPackageJsonImportsConditions_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPackageJsonImportsConditionsCase
{
    public static void TestAutoImportPackageJsonImportsConditions(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: node18\n// @Filename: /package.json\n{\n  \"imports\": {\n    \"#thing\": {\n        \"types\": { \"import\": \"./types-esm/thing.d.mts\", \"require\": \"./types/thing.d.ts\" },\n        \"default\": { \"import\": \"./esm/thing.mjs\", \"require\": \"./dist/thing.js\" }\n     }\n  }\n}\n// @Filename: /src/.ts\nsomething/*a*/\n// @Filename: /types/thing.d.ts\nexport function something(name: string): any;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "a", ["#thing"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
