// ported from: internal/fourslash/tests/javascriptModulesTypeImport_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JavascriptModulesTypeImportCase
{
    public static void TestJavascriptModulesTypeImport(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: types.js\n/**\n * @typedef {Object} Pet\n * @prop {string} name\n */\nmodule.exports = { a: 1 };\n// @Filename: app.js\n/**\n * @param { import(\"./types\")./**/ } p\n */\nfunction walk(p) {\n console.log(`Walking ${p.name}...`);\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["Pet"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
