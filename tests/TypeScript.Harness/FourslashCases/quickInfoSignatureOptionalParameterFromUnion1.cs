// ported from: internal/fourslash/tests/quickInfoSignatureOptionalParameterFromUnion1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoSignatureOptionalParameterFromUnion1Case
{
    public static void TestQuickInfoSignatureOptionalParameterFromUnion1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ndeclare const optionals:\n  | ((a?: { a: true }) => unknown)\n  | ((b?: { b: true }) => unknown);\n\n/**/optionals();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "", "const optionals: (arg0?: {\n    a: true;\n} & {\n    b: true;\n}) => unknown", "");
            }
            finally
            {
                done();
            }
        });
    }

}
