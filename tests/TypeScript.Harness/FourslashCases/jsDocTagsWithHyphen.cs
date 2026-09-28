// ported from: internal/fourslash/tests/jsDocTagsWithHyphen_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JsDocTagsWithHyphenCase
{
    public static void TestJsDocTagsWithHyphen(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: dummy.js\n/**\n * @typedef Product\n * @property {string} title\n * @property {boolean} h/*1*/igh-top some-comments\n */\n\n/**\n * @type {Pro/*2*/duct}\n */\nconst product = {\n    /*3*/\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "(property) high-top: boolean", "some-comments");
                f.VerifyQuickInfoAt(t, "2", "type Product = {\n    title: string;\n    \"high-top\": boolean;\n}", "");
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["\"high-top\""] } });
            }
            finally
            {
                done();
            }
        });
    }

}
