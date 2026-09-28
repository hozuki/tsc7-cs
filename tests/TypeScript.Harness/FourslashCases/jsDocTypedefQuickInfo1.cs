// ported from: internal/fourslash/tests/jsDocTypedefQuickInfo1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocTypedefQuickInfo1Case
{
    public static void TestJsDocTypedefQuickInfo1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: jsDocTypedef1.js\n/**\n * @typedef {Object} Opts\n * @property {string} x\n * @property {string=} y\n * @property {string} [z]\n * @property {string} [w=\"hi\"]\n * \n * @param {Opts} opts\n */\nfunction foo(/*1*/opts) {\n    opts.x;\n}\nfoo({x: 'abc'});\n/**\n * @typedef {object} Opts1\n * @property {string} x\n * @property {string=} y\n * @property {string} [z]\n * @property {string} [w=\"hi\"]\n * \n * @param {Opts1} opts\n */\nfunction foo1(/*2*/opts1) {\n    opts1.x;\n}\nfoo1({x: 'abc'});"u8);
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
