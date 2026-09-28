// ported from: internal/fourslash/tests/quickInfoJsDocTags15_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickInfoJsDocTags15Case
{
    public static void TestQuickInfoJsDocTags15(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @checkJs: true\n// @filename: /a.js\n/**\n * @callback Bar\n * @param {string} name\n * @returns {string}\n */\n\n/**\n * @typedef Foo\n * @property {Bar} getName\n */\nexport const foo = 1;\n// @filename: /b.js\nimport * as _a from \"./a.js\";\n/**\n * @implements {_a.Foo/*1*/}\n */\nclass C1 { }\n\n/**\n * @extends {_a.Foo/*2*/}\n */\nclass C2 { }\n\n/**\n * @augments {_a.Foo/*3*/}\n */\nclass C3 { }"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "/b.js");
                f.VerifyBaselineHover(t);
            }
            finally
            {
                done();
            }
        });
    }

}
