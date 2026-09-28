// ported from: internal/fourslash/tests/signatureHelpJSDocCallbackTagVS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpJSDocCallbackTagVSCase
{
    public static void TestSignatureHelpJSDocCallbackTagVS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n// @allowNonTsExtensions: true\n// @Filename: jsdocCallbackTag.js\n/**\n * @callback FooHandler - A kind of magic\n * @param {string} eventName - So many words\n * @param eventName2 {number | string} - Silence is golden\n * @param eventName3 - Osterreich mos def\n * @return {number} - DIVEKICK\n */\n/**\n * @type {FooHandler} callback\n */\nvar t;\n\n/**\n * @callback FooHandler2 - What, another one?\n * @param {string=} eventName - it keeps happening\n * @param {string} [eventName2] - i WARNED you dog\n */\n/**\n * @type {FooHandler2} callback\n */\nvar t2;\nt(/*4*/\"!\", /*5*/12, /*6*/false);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, new LSP.ClientCapabilities { VSSupportsVisualStudioExtensions = true }, content);
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
