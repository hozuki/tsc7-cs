// ported from: internal/fourslash/tests/explainFilesNodeNextWithTypesReference_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class ExplainFilesNodeNextWithTypesReferenceCase
{
    public static void TestExplainFilesNodeNextWithTypesReference(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /node_modules/react-hook-form/package.json\n{\n  \"name\": \"react-hook-form\",\n  \"main\": \"dist/index.cjs.js\",\n  \"module\": \"dist/index.esm.js\",\n  \"types\": \"dist/index.d.ts\",\n  \"exports\": {\n    \"./package.json\": \"./package.json\",\n    \".\": {\n      \"import\": \"./dist/index.esm.js\",\n      \"require\": \"./dist/index.cjs.js\",\n      \"types\": \"./dist/index.d.ts\"\n    }\n  }\n}\n// @Filename: /node_modules/react-hook-form/dist/index.cjs.js\nmodule.exports = {};\n// @Filename: /node_modules/react-hook-form/dist/index.esm.js\nexport function useForm() {}\n// @Filename: /node_modules/react-hook-form/dist/index.d.ts\n/// <reference types=\"react/**/\" />\nexport type Foo = React.Whatever;\nexport function useForm(): any;\n// @Filename: /node_modules/react/index.d.ts\ndeclare namespace JSX {}\ndeclare namespace React { export interface Whatever {} }\n// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"nodenext\",\n        \"explainFiles\": true\n    }\n    \"files\": [\"./index.ts\"]\n}\n// @Filename: /index.ts\nimport { useForm } from \"react-hook-form\";"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
