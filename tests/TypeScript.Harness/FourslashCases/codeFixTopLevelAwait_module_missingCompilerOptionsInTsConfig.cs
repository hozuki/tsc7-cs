// ported from: internal/fourslash/tests/codeFixTopLevelAwait_module_missingCompilerOptionsInTsConfig_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixTopLevelAwait_module_missingCompilerOptionsInTsConfigCase
{
    public static void TestCodeFixTopLevelAwait_module_missingCompilerOptionsInTsConfig(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /dir/a.ts\ndeclare const p: Promise<number>;\nawait p;\nexport {};\n// @filename: /dir/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"module\": \"commonjs\"\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t, "fixModuleOption");
            }
            finally
            {
                done();
            }
        });
    }

}
