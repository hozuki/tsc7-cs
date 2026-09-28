// ported from: internal/fourslash/tests/trailingCommaSignatureHelp_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class TrailingCommaSignatureHelpCase
{
    public static void TestTrailingCommaSignatureHelp(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function str(n: number): string;\n/**\n * Stringifies a number with radix\n * @param radix The radix\n */\nfunction str(n: number, radix: number): string;\nfunction str(n: number, radix?: number): string { return \"\"; }\n\nstr(1, /*a*/)\n\ndeclare function f<T>(a: T): T;\nf(2, /*b*/);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
