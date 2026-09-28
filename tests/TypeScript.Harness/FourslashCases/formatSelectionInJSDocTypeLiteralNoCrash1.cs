// ported from: internal/fourslash/tests/formatSelectionInJSDocTypeLiteralNoCrash1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FormatSelectionInJSDocTypeLiteralNoCrash1Case
{
    public static void TestFormatSelectionInJSDocTypeLiteralNoCrash1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @filename: index.js\n/**\n *\n *\n * @typedef {Object} Fixture\n * @property {typeof build} build\n/*begin*/ * @property {(url: string) => string} resolveUrl\n * @property {() => Promise<void>} clean\n/*end*/ * @property {(streaming?: boolean) => Promise<App>} loadTestAdapterApp\n */\n\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.FormatSelection(t, "begin", "end");
                f.VerifyCurrentFileContent(t, "/**\n" + " *\n" + " *\n" + " * @typedef {Object} Fixture\n" + " * @property {typeof build} build\n" + " * @property {(url: string) => string} resolveUrl\n" + " * @property {() => Promise<void>} clean\n" + " * @property {(streaming?: boolean) => Promise<App>} loadTestAdapterApp\n" + " */\n" + "\n");
            }
            finally
            {
                done();
            }
        });
    }

}
