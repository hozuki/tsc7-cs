// ported from: internal/fourslash/tests/tslibFindAllReferencesOnRuntimeImportWithPaths1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TslibFindAllReferencesOnRuntimeImportWithPaths1Case
{
    public static void TestTslibFindAllReferencesOnRuntimeImportWithPaths1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: project/src/foo.ts\nimport * as x from /**/\"tslib\";\n// @Filename: project/src/bar.ts\nexport default \"\";\n// @Filename: project/src/bal.ts\n\n// @Filename: project/src/dir/tslib.d.ts\nexport function __importDefault(...args: any): any;\nexport function __importStar(...args: any): any;\n// @Filename: project/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"moduleResolution\": \"node\",\n        \"module\": \"es2020\",\n        \"importHelpers\": true,\n        \"moduleDetection\": \"force\",\n        \"paths\": {\n            \"tslib\": [\"./src/dir/tslib\"]\n        }\n    }\n}"u8);
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
