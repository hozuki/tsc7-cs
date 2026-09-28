// ported from: internal/fourslash/tests/codeFixTopLevelForAwait_target_compatibleCompilerOptionsInTsConfig_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixTopLevelForAwait_target_compatibleCompilerOptionsInTsConfigCase
{
    public static void TestCodeFixTopLevelForAwait_target_compatibleCompilerOptionsInTsConfig(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @filename: /dir/a.ts\ndeclare const p: number[];\nfor await (const _ of p);\nexport {};\n// @filename: /dir/tsconfig.json\n{\n    \"compilerOptions\": {\n        \"target\": \"es2017\",\n        \"module\": \"esnext\"\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
