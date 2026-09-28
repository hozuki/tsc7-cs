// ported from: internal/fourslash/tests/javaScriptModules12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class JavaScriptModules12Case
{
    public static void TestJavaScriptModules12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowJs: true\n// @Filename: mod1.js\nvar x = require('fs');\n/*1*/\n// @Filename: mod2.js\nvar y;\nif(true) {\n    y = require('fs');\n}\n/*2*/\n// @Filename: glob1.js\nvar a = require;\n/*3*/\n// @Filename: glob2.js\nvar b = '';\n/*4*/\n// @Filename: consumer.js\n/*5*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["x", new LSP.CompletionItem { Label = "a", SortText = LS.SortTextGlobalsOrKeywords }, new LSP.CompletionItem { Label = "b", SortText = LS.SortTextGlobalsOrKeywords }], Excludes = ["y"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["y", new LSP.CompletionItem { Label = "a", SortText = LS.SortTextGlobalsOrKeywords }, new LSP.CompletionItem { Label = "b", SortText = LS.SortTextGlobalsOrKeywords }], Excludes = ["x"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["a", new LSP.CompletionItem { Label = "b", SortText = LS.SortTextGlobalsOrKeywords }], Excludes = ["x", "y"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", SortText = LS.SortTextGlobalsOrKeywords }, "b"], Excludes = ["x", "y"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["5"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", SortText = LS.SortTextGlobalsOrKeywords }, new LSP.CompletionItem { Label = "b", SortText = LS.SortTextGlobalsOrKeywords }], Excludes = ["x", "y"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
