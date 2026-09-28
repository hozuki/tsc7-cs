// ported from: internal/fourslash/tests/rewriteRelativeImportExtensionsProjectReferences1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RewriteRelativeImportExtensionsProjectReferences1Case
{
    public static void TestRewriteRelativeImportExtensionsProjectReferences1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: packages/common/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"lib\": [\"es5\"],\n        \"composite\": true,\n        \"rootDir\": \"src\",\n        \"outDir\": \"dist\",\n        \"module\": \"nodenext\",\n        \"resolveJsonModule\": false,\n    }\n}\n// @Filename: packages/common/package.json\n{\n    \"name\": \"common\",\n    \"version\": \"1.0.0\",\n    \"type\": \"module\",\n    \"exports\": {\n        \".\": {\n            \"source\": \"./src/index.ts\",\n            \"default\": \"./dist/index.js\"\n        }\n    }\n}\n// @Filename: packages/common/src/index.ts\nexport {};\n// @Filename: packages/main/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"nodenext\",\n        \"rewriteRelativeImportExtensions\": true,\n        \"lib\": [\"es5\"],\n        \"rootDir\": \"src\",\n        \"outDir\": \"dist\",\n        \"resolveJsonModule\": false,\n    },\n    \"references\": [\n        { \"path\": \"../common\" }\n    ]\n}\n// @Filename: packages/main/package.json\n{ \"type\": \"module\" }\n// @Filename: packages/main/src/index.ts\nimport {} from \"../../common/src/index.ts\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "/packages/main/src/index.ts");
                f.VerifyBaselineNonSuggestionDiagnostics(t);
            }
            finally
            {
                done();
            }
        });
    }

}
