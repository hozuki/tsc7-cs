// ported from: internal/fourslash/tests/commentsVariables_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CommentsVariablesCase
{
    public static void TestCommentsVariables(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("/** This is my variable*/\nvar myV/*1*/ariable = 10;\n/*2*/\n/** d variable*/\nvar d = 10;\nmyVariable = d;\n/*3*/\n/** foos comment*/\nfunction foo() {\n}\n/** fooVar comment*/\nvar foo/*12*/Var: () => void;\n/*4*/\nf/*5q*/oo(/*5*/);\nfo/*6q*/oVar(/*6*/);\nfo/*13*/oVar = f/*14*/oo;\n/*7*/\nf/*8q*/oo(/*8*/);\nfoo/*9q*/Var(/*9*/);\nvar fooVarVar = /*9aq*/fooVar;\n/**class comment*/\nclass c {\n    /** constructor comment*/\n    constructor() {\n    }\n}\n/**instance comment*/\nvar i = new c();\n/*10*/\n/** interface comments*/\ninterface i1 {\n}\n/**interface instance comments*/\nvar i1_i: i1;\n/*11*/\nfunction foo2(a: number): void;\nfunction foo2(b: string): void;\nfunction foo2(aOrb) {\n}\nvar x = fo/*15*/o2;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "var myVariable: number", "This is my variable");
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "myVariable", Detail = "var myVariable: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "This is my variable" } } }] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "myVariable", Detail = "var myVariable: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "This is my variable" } } }, new LSP.CompletionItem { Label = "d", Detail = "var d: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "d variable" } } }] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", Detail = "function foo(): void", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "foos comment" } } }, new LSP.CompletionItem { Label = "fooVar", Detail = "var fooVar: () => void", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "fooVar comment" } } }] } });
                f.GoToMarker(t, "5");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "foos comment" });
                f.VerifyQuickInfoAt(t, "5q", "function foo(): void", "foos comment");
                f.GoToMarker(t, "6");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "fooVar comment" });
                f.VerifyQuickInfoAt(t, "6q", "var fooVar: () => void", "fooVar comment");
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "foo", Detail = "function foo(): void", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "foos comment" } } }, new LSP.CompletionItem { Label = "fooVar", Detail = "var fooVar: () => void", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "fooVar comment" } } }] } });
                f.GoToMarker(t, "8");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "foos comment" });
                f.VerifyQuickInfoAt(t, "8q", "function foo(): void", "foos comment");
                f.GoToMarker(t, "9");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "fooVar comment" });
                f.VerifyQuickInfoAt(t, "9q", "var fooVar: () => void", "fooVar comment");
                f.VerifyQuickInfoAt(t, "9aq", "var fooVar: () => void", "fooVar comment");
                f.VerifyCompletions(t, "10", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "i", Detail = "var i: c", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "instance comment" } } }] } });
                f.VerifyCompletions(t, "11", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "i1_i", Detail = "var i1_i: i1", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "interface instance comments" } } }] } });
                f.VerifyQuickInfoAt(t, "12", "var fooVar: () => void", "fooVar comment");
                f.VerifyQuickInfoAt(t, "13", "var fooVar: () => void", "fooVar comment");
                f.VerifyQuickInfoAt(t, "14", "function foo(): void", "foos comment");
                f.VerifyQuickInfoAt(t, "15", "function foo2(a: number): void (+1 overload)", "");
            }
            finally
            {
                done();
            }
        });
    }

}
