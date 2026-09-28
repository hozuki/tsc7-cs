// ported from: internal/fourslash/tests/completionListInvalidMemberNames_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInvalidMemberNamesCase
{
    public static void TestCompletionListInvalidMemberNames(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("var x = {\n    \"foo \": \"space in the name\",\n    \"bar\": \"valid identifier name\",\n    \"break\": \"valid identifier name (matches a keyword)\",\n    \"any\": \"valid identifier name (matches a typescript keyword)\",\n    \"#\": \"invalid identifier name\",\n    \"$\": \"valid identifier name\",\n    \"\\u0062\": \"valid unicode identifier name (b)\",\n    \"\\u0031\\u0062\": \"invalid unicode identifier name (1b)\"\n};\n\nx[|./*a*/|];\nx[\"[|/*b*/|]\"];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "b", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "foo ", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "foo ", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "bar", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "bar", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "break", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "break", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "any", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "any", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "#", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "#", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "$", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "$", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "b", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "b", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "1b", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "1b", Range = f.Ranges()[1].LSRange } } }] } });
                f.VerifyCompletions(t, "a", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = [new LSP.CompletionItem { Label = "foo ", InsertText = "[\"foo \"]", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "[\"foo \"]", Range = f.Ranges()[0].LSRange } } }, "bar", "break", "any", new LSP.CompletionItem { Label = "#", InsertText = "[\"#\"]", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "[\"#\"]", Range = f.Ranges()[0].LSRange } } }, "$", "b", new LSP.CompletionItem { Label = "1b", InsertText = "[\"1b\"]", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "[\"1b\"]", Range = f.Ranges()[0].LSRange } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
