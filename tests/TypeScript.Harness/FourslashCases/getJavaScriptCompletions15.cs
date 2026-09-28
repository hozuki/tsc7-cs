// ported from: internal/fourslash/tests/getJavaScriptCompletions15_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class GetJavaScriptCompletions15Case
{
    public static void TestGetJavaScriptCompletions15(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @allowNonTsExtensions: true\n// @Filename: refFile1.ts\nexport var V = 1;\n// @Filename: refFile2.ts\nexport var V = \"123\"\n// @Filename: refFile3.ts\nexport var V = \"123\"\n// @Filename: main.js\nimport ref1 = require(\"./refFile1\");\nvar ref2 = require(\"./refFile2\");\nref1.V./*1*/;\nref2.V./*2*/;\nvar v = { x: require(\"./refFile3\") };\nv.x./*3*/;\nv.x.V./*4*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toExponential"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toLowerCase"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["V", new LSP.CompletionItem { Label = "ref1", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "ref2", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "require", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "v", SortText = LS.SortTextJavascriptIdentifiers }, new LSP.CompletionItem { Label = "x", SortText = LS.SortTextJavascriptIdentifiers }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["toLowerCase"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
