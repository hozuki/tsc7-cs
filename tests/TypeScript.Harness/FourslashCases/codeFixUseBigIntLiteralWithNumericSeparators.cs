// ported from: internal/fourslash/tests/codeFixUseBigIntLiteralWithNumericSeparators_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixUseBigIntLiteralWithNumericSeparatorsCase
{
    public static void TestCodeFixUseBigIntLiteralWithNumericSeparators(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("6_402_373_705_728_000;  // 18! < 2 ** 53\n0x16_BE_EC_CA_73_00_00; // 18! < 2 ** 53"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCodeFixNotAvailable(t, "useBigintLiteral");
            }
            finally
            {
                done();
            }
        });
    }

}
