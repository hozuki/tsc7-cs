// ported from: internal/fourslash/tests/getJavaScriptCompletions13_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetJavaScriptCompletions13Case
{
    public static void TestGetJavaScriptCompletions13(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: file1.js\nvar file1Identifier = 1;\ninterface Foo { FooProp: number };\n// @Filename: file2.js\nvar file2Identifier1 = 2;\nvar file2Identifier2 = 2;\n/*1*/\nfile2Identifier2./*2*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["file2Identifier1", "file2Identifier2", new LSP.CompletionItem { Label = "file1Identifier", SortText = LS.SortTextGlobalsOrKeywords }], Excludes = ["FooProp"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "file2Identifier1", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "file2Identifier2", SortText = LS.SortTextJavascriptIdentifiers }], Excludes = ["file1Identifier", "FooProp"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
