// ported from: internal/fourslash/tests/jsdocDeprecated_suggestion5_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsdocDeprecated_suggestion5Case
{
    public static void TestJsdocDeprecated_suggestion5(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @checkJs: true\n// @allowJs: true\n// @Filename: jsdocDeprecated_suggestion5.js\n/** @typedef {{ email: string, nickName?: string }} U2 */\n/** @type {U2} */\nconst u2 = { email: \"\" }\n/**\n * @callback K\n * @param {any} ctx\n * @return {void}\n */\n/** @type {K} */\nconst cc = _k => {}\n/** @enum {number} */\nconst DOOM = { e: 1, m: 1 }\n/** @type {DOOM} */\nconst kneeDeep = DOOM.e"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifySuggestionDiagnostics(t, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
