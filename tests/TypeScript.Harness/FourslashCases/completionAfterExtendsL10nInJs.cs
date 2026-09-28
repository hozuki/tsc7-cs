// ported from: internal/fourslash/tests/completionAfterExtendsL10nInJs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionAfterExtendsL10nInJsCase
{
    public static void TestCompletionAfterExtendsL10nInJs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("\n// @allowJs: true\n// @checkJs: true\n// @Filename: /interfaces.d.ts\nexport interface IL10n {}\n\n// @Filename: /genericl10n.js\n/** @typedef {import(\"./interfaces\").IL10n} IL10n */\n\nclass L10n {\n\tconstructor(options) {\n\t\tthis.options = options;\n\t}\n}\n\n/**\n * @implements {IL10n}\n */\nclass GenericL10n extends L10n/*1*/ {\n\tconstructor(lang) {\n\t\tsuper({ lang });\n\t}\n}\n\n"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "1");
                f.GetCompletions(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
