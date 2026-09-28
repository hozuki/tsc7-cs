// ported from: internal/fourslash/tests/commentsExternalModulesFourslash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CommentsExternalModulesFourslashCase
{
    public static void TestCommentsExternalModulesFourslash(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: commentsExternalModules_file0.ts\n/** Namespace comment*/\nexport namespace m/*1*/1 {\n    /** b's comment*/\n    export var b: number;\n    /** foo's comment*/\n    function foo() {\n        return /*2*/b;\n    }\n    /** m2 comments*/\n    export namespace m2 {\n        /** class comment;*/\n        export class c {\n        };\n        /** i*/\n        export var i = new c();\n    }\n    /** exported function*/\n    export function fooExport() {\n        return f/*3q*/oo(/*3*/);\n    }\n}\n/*4*/m1./*5*/fooEx/*6q*/port(/*6*/);\nvar my/*7*/var = new m1.m2./*8*/c();\n// @Filename: commentsExternalModules_file1.ts\n/**This is on import declaration*/\nimport ex/*9*/tMod = require(\"./commentsExternalModules_file0\");\n/*10*/extMod./*11*/m1./*12*/fooExp/*13q*/ort(/*13*/);\nvar new/*14*/Var = new extMod.m1.m2./*15*/c();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToFile(t, "commentsExternalModules_file0.ts");
                f.VerifyQuickInfoAt(t, "1", "namespace m1", "Namespace comment");
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "b", Detail = "var b: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "b's comment" } } }, new LSP.CompletionItem { Label = "foo", Detail = "function foo(): number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "foo's comment" } } }] } });
                f.GoToMarker(t, "3");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "foo's comment" });
                f.VerifyQuickInfoAt(t, "3q", "function foo(): number", "foo's comment");
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "m1", Detail = "namespace m1", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Namespace comment" } } }] } });
                f.VerifyCompletions(t, "5", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "b", Detail = "var m1.b: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "b's comment" } } }, new LSP.CompletionItem { Label = "fooExport", Detail = "function m1.fooExport(): number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "exported function" } } }, new LSP.CompletionItem { Label = "m2", Detail = "namespace m1.m2", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "m2 comments" } } }] } });
                f.GoToMarker(t, "6");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "exported function" });
                f.VerifyQuickInfoAt(t, "6q", "function m1.fooExport(): number", "exported function");
                f.VerifyQuickInfoAt(t, "7", "var myvar: m1.m2.c", "");
                f.VerifyCompletions(t, "8", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "c", Detail = "constructor m1.m2.c(): m1.m2.c", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "class comment;" } } }, new LSP.CompletionItem { Label = "i", Detail = "var m1.m2.i: m1.m2.c", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "i" } } }] } });
                f.GoToFile(t, "commentsExternalModules_file1.ts");
                f.VerifyQuickInfoAt(t, "9", "import extMod = require(\"./commentsExternalModules_file0\")", "This is on import declaration");
                f.VerifyCompletions(t, "10", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "extMod", Detail = "import extMod = require(\"./commentsExternalModules_file0\")", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "This is on import declaration" } } }] } });
                f.VerifyCompletions(t, "11", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "m1", Detail = "namespace extMod.m1", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Namespace comment" } } }] } });
                f.VerifyCompletions(t, "12", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "b", Detail = "var extMod.m1.b: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "b's comment" } } }, new LSP.CompletionItem { Label = "fooExport", Detail = "function extMod.m1.fooExport(): number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "exported function" } } }, new LSP.CompletionItem { Label = "m2", Detail = "namespace extMod.m1.m2", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "m2 comments" } } }] } });
                f.GoToMarker(t, "13");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "exported function" });
                f.VerifyQuickInfoAt(t, "13q", "function extMod.m1.fooExport(): number", "exported function");
                f.VerifyQuickInfoAt(t, "14", "var newVar: extMod.m1.m2.c", "");
                f.VerifyCompletions(t, "15", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "c", Detail = "constructor extMod.m1.m2.c(): extMod.m1.m2.c", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "class comment;" } } }, new LSP.CompletionItem { Label = "i", Detail = "var extMod.m1.m2.i: extMod.m1.m2.c", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "i" } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
