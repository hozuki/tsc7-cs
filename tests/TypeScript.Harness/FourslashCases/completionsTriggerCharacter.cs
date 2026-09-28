// ported from: internal/fourslash/tests/completionsTriggerCharacter_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsTriggerCharacterCase
{
    public static void TestCompletionsTriggerCharacter(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @jsx: preserve\n/** @/*tag*/ */\n//</*comment*/\nconst x: \"a\" | \"b\" = \"[|/*openQuote*/|]\"/*closeQuote*/;\nconst y: 'a' | 'b' = '[|/*openSingleQuote*/|]'/*closeSingleQuote*/;\nconst z: 'a' | 'b' = `[|/*openTemplate*/|]`/*closeTemplate*/;\nconst q: \"`a`\" | \"`b`\" = \"[|`/*openTemplateInQuote*/a`/*closeTemplateInQuote*/|]\";\n// \"/*quoteInComment*/ </*lessInComment*/\n// @Filename: /foo/importMe.ts\nwhatever\n// @Filename: /a.tsx\ndeclare global {\n    namespace JSX {\n        interface Element {}\n        interface IntrinsicElements {\n            div: {};\n        }\n    }\n}\nconst ctr = </*openTag*/;\nconst less = 1 </*lessThan*/;\nconst closeTag = <div> foo <//*closeTag*/;\nimport something from \"./foo//*path*/\";\nconst divide = 1 //*divide*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "tag", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["param"] } });
                f.VerifyCompletions(t, "comment", null!);
                f.VerifyCompletions(t, "openQuote", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "a", Range = f.Ranges()[0].LSRange } } }, new LSP.CompletionItem { Label = "b", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "b", Range = f.Ranges()[0].LSRange } } }] } });
                f.VerifyCompletions(t, "closeQuote", null!);
                f.VerifyCompletions(t, "openSingleQuote", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "a", Range = f.Ranges()[1].LSRange } } }, new LSP.CompletionItem { Label = "b", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "b", Range = f.Ranges()[1].LSRange } } }] } });
                f.VerifyCompletions(t, "closeSingleQuote", null!);
                f.VerifyCompletions(t, "openTemplate", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "a", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "a", Range = f.Ranges()[2].LSRange } } }, new LSP.CompletionItem { Label = "b", TextEdit = new LSP.TextEditOrInsertReplaceEdit { TextEdit = new LSP.TextEdit { NewText = "b", Range = f.Ranges()[2].LSRange } } }] } });
                f.VerifyCompletions(t, "closeTemplate", null!);
                f.VerifyCompletions(t, "quoteInComment", null!);
                f.VerifyCompletions(t, "lessInComment", null!);
                f.VerifyCompletions(t, "openTag", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["div"] } });
                f.VerifyCompletions(t, "lessThan", null!);
                f.VerifyCompletions(t, "closeTag", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["div>"] } });
                f.VerifyCompletions(t, "path", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["importMe"] } });
                f.VerifyCompletions(t, "divide", null!);
            }
            finally
            {
                done();
            }
        });
    }

}
