// ported from: internal/fourslash/tests/signatureHelpTaggedTemplatesNegatives1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpTaggedTemplatesNegatives1Case
{
    public static void TestSignatureHelpTaggedTemplatesNegatives1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("function f(templateStrings, x, y, z) { return 10; }\nfunction g(templateStrings, x, y, z) { return \"\"; }\n\n/*1*/f/*2*/ /*3*/` qwerty ${ 123 } asdf ${   41234   }  zxcvb ${ g `    ` }     `/*4*/"u8);
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
