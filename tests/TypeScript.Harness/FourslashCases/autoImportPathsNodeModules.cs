// ported from: internal/fourslash/tests/autoImportPathsNodeModules_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportPathsNodeModulesCase
{
    public static void TestAutoImportPathsNodeModules(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"commonjs\",\n        \"moduleResolution\": \"node\",\n        \"rootDir\": \"ts\",\n        \"baseUrl\": \".\",\n        \"paths\": {\n            \"*\": [\"node_modules/@woltlab/wcf/ts/*\"]\n        }\n    },\n    \"include\": [\n        \"ts\",\n        \"node_modules/@woltlab/wcf/ts\",\n     ]\n}\n// @Filename: node_modules/@woltlab/wcf/ts/WoltLabSuite/Core/Component/Dialog.ts\nexport class Dialog {}\n// @Filename: ts/main.ts\nDialog/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["WoltLabSuite/Core/Component/Dialog"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
