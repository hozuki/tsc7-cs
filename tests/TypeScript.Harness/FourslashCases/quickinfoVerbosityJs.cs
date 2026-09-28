// ported from: internal/fourslash/tests/quickinfoVerbosityJs_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class QuickinfoVerbosityJsCase
{
    public static void TestQuickinfoVerbosityJs(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: somefile.js\n// @allowJs: true\n/**\n * @typedef {Object} SomeType\n * @property {string} prop1\n */\n/** @type {SomeType} */\nconst a/*1*/ = {\n    prop1: 'value',\n}\n/**\n * @typedef {Object} SomeType2/*2*/\n * @property {number} prop2\n * @property {SomeType} prop3\n */\n/** @type {SomeType[]} */\nconst ss = [{ prop1: 'value' }, { prop1: 'value' }];\nconst d = ss.map((s/*3*/) => s.prop1);\n/** @param {SomeType} a\n * @returns {SomeType}\n */\nfunction someFun/*4*/(a) {\n    return a;\n}\nsomeFun.what = 'what';\nclass SomeClass/*5*/ {\n    /** @type {SomeType2} */\n    b;\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineHoverWithVerbosity(t, new Dictionary<string, int[]> { ["1"] = [0, 1], ["2"] = [0, 1], ["3"] = [0, 1], ["4"] = [0, 1], ["5"] = [0, 1, 2] });
            }
            finally
            {
                done();
            }
        });
    }

}
