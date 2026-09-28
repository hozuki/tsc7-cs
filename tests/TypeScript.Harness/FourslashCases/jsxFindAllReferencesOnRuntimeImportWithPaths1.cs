// ported from: internal/fourslash/tests/jsxFindAllReferencesOnRuntimeImportWithPaths1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsxFindAllReferencesOnRuntimeImportWithPaths1Case
{
    public static void TestJsxFindAllReferencesOnRuntimeImportWithPaths1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: project/src/foo.ts\nimport * as x from /**/\"@foo/dir/jsx-runtime\";\n// @Filename: project/src/bar.tsx\nexport default <div></div>;\n// @Filename: project/src/baz.tsx\nexport default <></>;\n// @Filename: project/src/bam.tsx\nexport default <script src=\"\"/>;\n// @Filename: project/src/bat.tsx\nexport const a = 1;\n// @Filename: project/src/bal.tsx\n\n// @Filename: project/src/dir/jsx-runtime.ts\nexport {}\n// @Filename: project/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"moduleResolution\": \"node\",\n        \"module\": \"es2020\",\n        \"jsx\": \"react-jsx\",\n        \"jsxImportSource\": \"@foo/dir\",\n        \"moduleDetection\": \"force\",\n        \"paths\": {\n            \"@foo/dir/jsx-runtime\": [\"./src/dir/jsx-runtime\"]\n        }\n    }\n}"u8);
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
