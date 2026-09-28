// ported from: internal/fourslash/tests/codeFixClassImplementInterfaceMultipleImplements2_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CodeFixClassImplementInterfaceMultipleImplements2Case
{
    public static void TestCodeFixClassImplementInterfaceMultipleImplements2(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\ninterface I1 {\n    x: number;\n}\ninterface I2 {\n    y: \"\U000232DD\u0AA2\u0234\u00AC\u23CA\";\n}\n\nclass C implements I1,I2 {[|\n    |]x: number;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyRangeAfterCodeFix(t, "\ny: \"\ud84c\udedd\u0aa2\u0234\u00ac\u23ca\";\n", false, 0, 0);
                f.VerifyCodeFixNotAvailable(t);
            }
            finally
            {
                done();
            }
        });
    }

}
