// ported from: internal/fourslash/tests/findAllRefs_importType_js4_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class FindAllRefs_importType_js4Case
{
    public static void TestFindAllRefs_importType_js4(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @module: commonjs\n// @allowJs: true\n// @checkJs: true\n// @Filename: /a.js\n/**\n * @callback /**/A\n * @param {unknown} response\n */\n\nmodule.exports = {};\n// @Filename: /b.js\n/** @typedef {import(\"./a\").A} A */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyBaselineFindAllReferences(t, "");
            }
            finally
            {
                done();
            }
        });
    }

}
