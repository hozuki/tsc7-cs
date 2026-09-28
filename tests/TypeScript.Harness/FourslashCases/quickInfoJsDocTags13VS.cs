// ported from: internal/fourslash/tests/quickInfoJsDocTags13VS_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTags13VSCase
{
    public static void TestQuickInfoJsDocTags13VS(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @filename: ./a.js\n/**\n * First overload\n * @overload\n * @param {number} a\n * @returns {void}\n */\n\n/**\n * Second overload\n * @overload\n * @param {string} a\n * @returns {void}\n */\n\n/**\n * @param {string | number} a\n * @returns {void}\n */\nfunction f(a) {}\n\nf(/*a*/1);\nf(/*b*/\"\");"u8);
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
