// ported from: internal/fourslash/tests/rewriteRelativeImportExtensionsProjectReferences3_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class RewriteRelativeImportExtensionsProjectReferences3Case
{
    public static void TestRewriteRelativeImportExtensionsProjectReferences3(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: src/tsconfig-base.json\n{\n    \"compilerOptions\": {\n        \"lib\": [\"es5\"],\n        \"module\": \"nodenext\",\n        \"composite\": true,\n        \"rewriteRelativeImportExtensions\": true,\n    }\n}\n// @Filename: src/compiler/tsconfig.json\n{\n    \"extends\": \"../tsconfig-base.json\",\n    \"compilerOptions\": {\n        \"lib\": [\"es5\"],\n        \"rootDir\": \".\",\n        \"outDir\": \"../../dist/compiler\",\n}\n// @Filename: src/compiler/parser.ts\nexport {};\n// @Filename: src/services/tsconfig.json\n{\n    \"extends\": \"../tsconfig-base.json\",\n    \"compilerOptions\": {\n        \"lib\": [\"es5\"],\n        \"rootDir\": \".\",\n        \"outDir\": \"../../dist/services\",\n    },\n    \"references\": [\n        { \"path\": \"../compiler\" }\n    ]\n}\n// @Filename: src/services/services.ts\nimport {} from \"../compiler/parser.ts\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.MarkTestAsStradaServer();
                f.GoToFile(t, "/src/services/services.ts");
                f.VerifyBaselineNonSuggestionDiagnostics(t);
            }
            finally
            {
                done();
            }
        });
    }

}
