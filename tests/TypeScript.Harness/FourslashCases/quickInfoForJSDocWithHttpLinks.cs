// ported from: internal/fourslash/tests/quickInfoForJSDocWithHttpLinks_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoForJSDocWithHttpLinksCase
{
    public static void TestQuickInfoForJSDocWithHttpLinks(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @filename: quickInfoForJSDocWithHttpLinks.js\n/** @typedef {number} /*1*/https://wat */\n\n/**\n* @typedef {Object} Oops\n* @property {number} /*2*/https://wass\n*/\n\n\n/** @callback /*3*/http://vad */\n\n/** @see https://hvad */\nvar /*4*/see1 = true\n\n/** @see {@link https://hva} */\nvar /*5*/see2 = true\n\n/** {@link https://hvaD} */\nvar /*6*/see3 = true"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
