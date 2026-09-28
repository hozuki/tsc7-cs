// ported from: internal/fourslash/tests/autoImportRootDirs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class AutoImportRootDirsCase
{
    public static void TestAutoImportRootDirs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"commonjs\",\n        \"rootDirs\": [\".\", \"./some/other/root\"]\n    }\n}\n// @Filename: /some/other/root/types.ts\nexport type Something = {};\n// @Filename: /index.ts\nconst s: Something/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyImportFixModuleSpecifiers(t, "", ["./types"], null!);
            }
            finally
            {
                done();
            }
        });
    }

}
