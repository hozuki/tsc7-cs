// ported from: internal/fourslash/tests/packageJsonImportsFailedLookups_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class PackageJsonImportsFailedLookupsCase
{
    public static void TestPackageJsonImportsFailedLookups(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /a/b/c/d/e/tsconfig.json\n{ \"compilerOptions\": { \"lib\": [\"es5\"], \"module\": \"nodenext\" } }\n// @Filename: /a/b/c/d/e/package.json\n{\n  \"name\": \"app\",\n  \"imports\": {\n    \"#utils\": \"lodash\"\n  }\n}\n// @Filename: /a/b/node_modules/lodash/index.d.ts\nexport function add(a: number, b: number): number;\n// @Filename: /a/b/c/d/e/index.ts\nimport { add } from \"#utils\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "/a/b/c/d/e/index.ts");
            }
            finally
            {
                done();
            }
        });
    }

}
