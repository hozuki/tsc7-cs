// ported from: internal/fourslash/tests/signatureHelpInferenceJsDocImportTagVS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpInferenceJsDocImportTagVSCase
{
    public static void TestSignatureHelpInferenceJsDocImportTagVS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJS: true\n// @checkJs: true\n// @module: esnext\n// @filename: a.ts\nexport interface Foo {}\n// @filename: b.js\n/**\n * @import {\n *     Foo\n * } from './a'\n */\n\n/**\n * @param {Foo} a\n */\nfunction foo(a) {}\nfoo(/**/)"u8);
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
