// ported from: internal/fourslash/tests/commentsImportDeclaration_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CommentsImportDeclarationCase
{
    public static void TestCommentsImportDeclaration(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @Filename: commentsImportDeclaration_file0.ts\n/** NamespaceComment*/\nexport namespace m/*2*/1 {\n    /** b's comment*/\n    export var b: number;\n    /** m2 comments*/\n    export namespace m2 {\n        /** class comment;*/\n        export class c {\n        };\n        /** i*/\n        export var i: c;;\n    }\n    /** exported function*/\n    export function fooExport(): number;\n}\n// @Filename: commentsImportDeclaration_file1.ts\n///<reference path='commentsImportDeclaration_file0.ts'/>\n/** Import declaration*/\nimport /*3*/extMod = require(\"./commentsImportDeclaration_file0/*4*/\");\nextMod./*6*/m1./*7*/fooEx/*8q*/port(/*8*/);\nvar new/*9*/Var = new extMod.m1.m2./*10*/c();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "2", "namespace m1", "NamespaceComment");
                f.VerifyQuickInfoAt(t, "3", "import extMod = require(\"./commentsImportDeclaration_file0\")", "Import declaration");
                f.VerifyCompletions(t, "6", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "m1", Detail = "namespace extMod.m1", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "NamespaceComment" } } }] } });
                f.VerifyCompletions(t, "7", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "b", Detail = "var extMod.m1.b: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "b's comment" } } }, new LSP.CompletionItem { Label = "fooExport", Detail = "function extMod.m1.fooExport(): number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "exported function" } } }, new LSP.CompletionItem { Label = "m2", Detail = "namespace extMod.m1.m2", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "m2 comments" } } }] } });
                f.GoToMarker(t, "8");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "exported function" });
                f.VerifyQuickInfoAt(t, "8q", "function extMod.m1.fooExport(): number", "exported function");
                f.VerifyQuickInfoAt(t, "9", "var newVar: extMod.m1.m2.c", "");
                f.VerifyCompletions(t, "10", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "c", Detail = "constructor extMod.m1.m2.c(): extMod.m1.m2.c", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "class comment;" } } }, new LSP.CompletionItem { Label = "i", Detail = "var extMod.m1.m2.i: extMod.m1.m2.c", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "i" } } }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
