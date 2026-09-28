// ported from: internal/fourslash/tests/quickInfoSignatureRestParameterFromUnion4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoSignatureRestParameterFromUnion4Case
{
    public static void TestQuickInfoSignatureRestParameterFromUnion4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("declare const fn:\n  | ((a?: { x: number }, b?: { x: number }) => number)\n  | ((...a: { y: number }[]) => number);\n\n/**/fn();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "const fn: (a?: {\n    x: number;\n} & {\n    y: number;\n}, b?: {\n    x: number;\n} & {\n    y: number;\n}, ...args: {\n    y: number;\n}[]) => number", "");
            }
            finally
            {
                done();
            }
        });
    }

}
