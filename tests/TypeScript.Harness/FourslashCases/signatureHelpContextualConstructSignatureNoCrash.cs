// ported from: internal/fourslash/tests/signatureHelpContextualConstructSignatureNoCrash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpContextualConstructSignatureNoCrashCase
{
    public static void TestSignatureHelpContextualConstructSignatureNoCrash(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\ntype Obj = {\n    foo: new () => object\n}\n\nlet obj: Obj = {\n    foo(/*constructOnly*/) {}\n}\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "constructOnly");
                f.VerifyNoSignatureHelp(t);
            }
            finally
            {
                done();
            }
        });
    }

}
