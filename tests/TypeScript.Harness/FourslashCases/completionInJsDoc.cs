// ported from: internal/fourslash/tests/completionInJsDoc_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionInJsDocCase
{
    public static void TestCompletionInJsDoc(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: Foo.js\n/** @/*1*/ */\nvar v1;\n\n/** @p/*2*/ */\nvar v2;\n\n/** @param /*3*/ */\nvar v3;\n\n/** @param { n/*4*/ } bar */\nvar v4;\n\n/** @type { n/*5*/ } */\nvar v5;\n\n// @/*6*/\nvar v6;\n\n// @pa/*7*/\nvar v7;\n\n/** @return { n/*8*/ } */\nvar v8;\n\n/** /*9*/ */\n\n/**\n /*10*/\n*/\n\n/**\n * /*11*/\n */\n\n/**\n          /*12*/\n */\n\n/**\n  *       /*13*/\n  */\n\n/**\n  * some comment /*14*/\n  */\n\n/**\n  * @param /*15*/\n  */\n\n/** @param /*16*/ */\n\n/**\n  * jsdoc inline tag {@/*17*/}\n  */"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1", "2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["constructor", "param", "type", "method", "template"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["3", "15", "16"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["4", "5", "8"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "number", SortText = LS.SortTextGlobalsOrKeywords }] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["6", "7", "14"] }, null!);
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["9", "10", "11", "12", "13"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["@argument", "@returns"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["17"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["link", "tutorial"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
