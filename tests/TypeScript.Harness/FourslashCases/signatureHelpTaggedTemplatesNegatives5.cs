// ported from: internal/fourslash/tests/signatureHelpTaggedTemplatesNegatives5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpTaggedTemplatesNegatives5Case
{
    public static void TestSignatureHelpTaggedTemplatesNegatives5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function foo(strs, ...rest) {\n}\n\n/*1*/fo/*2*/o /*3*/`abcd`/*4*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyNoSignatureHelpForMarkers(t, f.MarkerNames());
            }
            finally
            {
                done();
            }
        });
    }

}
