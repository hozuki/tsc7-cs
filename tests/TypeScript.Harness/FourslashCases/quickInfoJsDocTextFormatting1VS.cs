// ported from: internal/fourslash/tests/quickInfoJsDocTextFormatting1VS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTextFormatting1VSCase
{
    public static void TestQuickInfoJsDocTextFormatting1VS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/**\n * @param {number} var1 **Highlighted text**\n * @param {string} var2 Another **Highlighted text**\n*/\nfunction f1(var1, var2) { }\n\n/**\n * @param {number} var1 *Regular text with an asterisk\n * @param {string} var2 Another *Regular text with an asterisk\n*/\nfunction f2(var1, var2) { }\n\n/**\n * @param {number} var1 \n * *Regular text with an asterisk\n * @param {string} var2 \n * Another *Regular text with an asterisk\n*/\nfunction f3(var1, var2) { }\n\n/**\n * @param {number} var1 \n * **Highlighted text**\n * @param {string} var2 \n * Another **Highlighted text**\n*/\nfunction f4(var1, var2) { }\n\n/**\n * @param {number} var1 \n   **Highlighted text**\n * @param {string} var2 \n   Another **Highlighted text**\n*/\nfunction f5(var1, var2) { }\n\nf1(/*1*/);\nf2(/*2*/);\nf3(/*3*/);\nf4(/*4*/);\nf5(/*5*/);"u8);
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
