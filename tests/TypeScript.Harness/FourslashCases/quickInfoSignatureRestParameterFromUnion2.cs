// ported from: internal/fourslash/tests/quickInfoSignatureRestParameterFromUnion2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoSignatureRestParameterFromUnion2Case
{
    public static void TestQuickInfoSignatureRestParameterFromUnion2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ndeclare const rest:\n  | ((a?: { a: true }, ...rest: string[]) => unknown)\n  | ((b?: { b: true }) => unknown);\n\n/**/rest({ a: true, b: true }, \"foo\", \"bar\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "const rest: (arg0?: {\n    a: true;\n} & {\n    b: true;\n}, ...rest: string[]) => unknown", "");
            }
            finally
            {
                done();
            }
        });
    }

}
