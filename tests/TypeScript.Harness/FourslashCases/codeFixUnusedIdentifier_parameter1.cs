// ported from: internal/fourslash/tests/codeFixUnusedIdentifier_parameter1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixUnusedIdentifier_parameter1Case
{
    public static void TestCodeFixUnusedIdentifier_parameter1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @noUnusedLocals: true\n// @noUnusedParameters: true\nfunction g(a, b) { b; }\ng(1, 2);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t, "Remove unused declaration for: 'a'");
            }
            finally
            {
                done();
            }
        });
    }

}
