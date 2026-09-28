// ported from: internal/fourslash/tests/completionForStringLiteral_details_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionForStringLiteral_detailsCase
{
    public static void TestCompletionForStringLiteral_details(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: /other.ts\nexport const x = 0;\n// @Filename: /a.ts\nimport {} from \".//*path*/\";\n\nconst x: \"a\" = \"[|/*type*/|]\";\n\ninterface I {\n    /** Prop doc */\n    x: number;\n    /** Method doc */\n    m(): void;\n}\ndeclare const o: I;\no[\"[|/*prop*/|]\"];"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "path", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "other", Detail = "other.ts", Kind = LSP.CompletionItemKind.File }] } });
                f.VerifyCompletions(t, "type", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a", Detail = "a", Kind = LSP.CompletionItemKind.Constant, TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "a", Range = f.Ranges()[0].LSRange } } }] } });
                f.VerifyCompletions(t, "prop", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "m", Detail = "(method) I.m(): void", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Method doc" } }, Kind = LSP.CompletionItemKind.Method, TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "m", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "x", Detail = "(property) I.x: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Prop doc" } }, Kind = LSP.CompletionItemKind.Field, TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "x", Range = f.Ranges()[1].LSRange } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
