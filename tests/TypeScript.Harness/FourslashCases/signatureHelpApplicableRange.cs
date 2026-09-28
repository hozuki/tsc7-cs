// ported from: internal/fourslash/tests/signatureHelpApplicableRange_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpApplicableRangeCase
{
    public static void TestSignatureHelpApplicableRange(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("let obj = {\n    foo(s: string): string {\n        return s;\n    }\n};\n\nlet s =/*a*/ obj.foo(\"Hello, world!\")/*b*/  \n  /*c*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoSignatureHelpForMarkers(t, "a", "b", "c");
            }
            finally
            {
                done();
            }
        });
    }

}
