// ported from: internal/fourslash/tests/jsDocFunctionSignatures5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocFunctionSignatures5Case
{
    public static void TestJsDocFunctionSignatures5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n// @allowJs: true\n// @Filename: Foo.js\n/**\n * Filters a path based on a regexp or glob pattern.\n * @param {String} basePath The base path where the search will be performed.\n * @param {String} pattern A string defining a regexp of a glob pattern.\n * @param {String} type The search pattern type, can be a regexp or a glob.\n * @param {Object} options A object containing options to the search.\n * @return {Array} A list containing the filtered paths.\n */\nfunction pathFilter(basePath, pattern, type, options){\n//...\n}\npathFilter(/**/'foo', 'bar', 'baz', {});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
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
