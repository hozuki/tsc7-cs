// ported from: internal/fourslash/tests/signatureHelpIteratorNextVS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class SignatureHelpIteratorNextVSCase
{
    public static void TestSignatureHelpIteratorNextVS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: esnext\ndeclare const iterator: Iterator<string, void, number>;\n\niterator.next(/*1*/);\niterator.next(/*2*/ 0);\n\ndeclare const generator: Generator<string, void, number>;\n\ngenerator.next(/*3*/);\ngenerator.next(/*4*/ 0);\n\ndeclare const asyncIterator: AsyncIterator<string, void, number>;\n\nasyncIterator.next(/*5*/);\nasyncIterator.next(/*6*/ 0);\n\ndeclare const asyncGenerator: AsyncGenerator<string, void, number>;\n\nasyncGenerator.next(/*7*/);\nasyncGenerator.next(/*8*/ 0);"u8);
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
