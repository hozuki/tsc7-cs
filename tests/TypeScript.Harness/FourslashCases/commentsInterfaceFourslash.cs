// ported from: internal/fourslash/tests/commentsInterfaceFourslash_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CommentsInterfaceFourslashCase
{
    public static void TestCommentsInterfaceFourslash(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\n/** this is interface 1*/\ninterface i/*1*/1 {\n}\nvar i1/*2*/_i: i1;\ninterface nc_/*3*/i1 {\n}\nvar nc_/*4*/i1_i: nc_i1;\n/** this is interface 2 with members*/\ninterface i/*5*/2 {\n    /** this is x*/\n    x: number;\n    /** this is foo*/\n    foo: (/**param help*/b: number) => string;\n    /** this is indexer*/\n    [/**string param*/i: string]: number;\n    /**new method*/\n    new (/** param*/i: i1);\n    nc_x: number;\n    nc_foo: (b: number) => string;\n    [i: number]: number;\n    /** this is call signature*/\n    (/**paramhelp a*/a: number,/**paramhelp b*/ b: number) : number;\n    /** this is fnfoo*/\n    fnfoo(/**param help*/b: number): string;\n    nc_fnfoo(b: number): string;\n}\nvar i2/*6*/_i: /*34i*/i2;\nvar i2_i/*7*/_x = i2_i./*8*/x;\nvar i2_i/*9*/_foo = i2_i.f/*10*/oo;\nvar i2_i_f/*11*/oo_r = i2_i.f/*12q*/oo(/*12*/30);\nvar i2_i_i2_/*13*/si = i2/*13q*/_i[\"hello\"];\nvar i2_i_i2/*14*/_ii = i2/*14q*/_i[30];\nvar i2_/*15*/i_n = new i2/*16q*/_i(/*16*/i1_i);\nvar i2_i/*17*/_nc_x = i2_i.n/*18*/c_x;\nvar i2_i_/*19*/nc_foo = i2_i.n/*20*/c_foo;\nvar i2_i_nc_f/*21*/oo_r = i2_i.nc/*22q*/_foo(/*22*/30);\nvar i2/*23*/_i_r = i2/*24q*/_i(/*24*/10, /*25*/20);\nvar i2_i/*26*/_fnfoo = i2_i.fn/*27*/foo;\nvar i2_i_/*28*/fnfoo_r = i2_i.fn/*29q*/foo(/*29*/10);\nvar i2_i/*30*/_nc_fnfoo = i2_i.nc_fn/*31*/foo;\nvar i2_i_nc_/*32*/fnfoo_r = i2_i.nc/*33q*/_fnfoo(/*33*/10);\n/*34*/\ninterface i3 {\n    /** Comment i3 x*/\n    x: number;\n    /** Function i3 f*/\n    f(/**number parameter*/a: number): string;\n    /** i3 l*/\n    l: (/**comment i3 l b*/b: number) => string;\n    nc_x: number;\n    nc_f(a: number): string;\n    nc_l: (b: number) => string;\n}\nvar i3_i: i3;\ni3_i = {\n    /*35*/f: /**own f*/ (/**i3_i a*/a: number) => \"Hello\" + /*36*/a,\n    l: this./*37*/f,\n    /** own x*/\n    x: this.f(/*38*/10),\n    nc_x: this.l(/*39*/this.x),\n    nc_f: this.f,\n    nc_l: this.l\n};\n/*40*/i/*40q*/3_i./*41*/f(/*42*/10);\ni3_i./*43q*/l(/*43*/10);\ni3_i.nc_/*44q*/f(/*44*/10);\ni3_i.nc/*45q*/_l(/*45*/10);"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyQuickInfoAt(t, "1", "interface i1", "this is interface 1");
                f.VerifyQuickInfoAt(t, "2", "var i1_i: i1", "");
                f.VerifyQuickInfoAt(t, "3", "interface nc_i1", "");
                f.VerifyQuickInfoAt(t, "4", "var nc_i1_i: nc_i1", "");
                f.VerifyQuickInfoAt(t, "5", "interface i2", "this is interface 2 with members");
                f.VerifyQuickInfoAt(t, "6", "var i2_i: i2", "");
                f.VerifyQuickInfoAt(t, "7", "var i2_i_x: number", "");
                f.VerifyQuickInfoAt(t, "8", "(property) i2.x: number", "this is x");
                f.VerifyCompletions(t, "8", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersWithPrototypePlus([new LSP.CompletionItem { Label = "x", Detail = "(property) i2.x: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "this is x" } } }, new LSP.CompletionItem { Label = "foo", Detail = "(property) i2.foo: (b: number) => string", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "this is foo" } } }, new LSP.CompletionItem { Label = "nc_x", Detail = "(property) i2.nc_x: number" }, new LSP.CompletionItem { Label = "nc_foo", Detail = "(property) i2.nc_foo: (b: number) => string" }, new LSP.CompletionItem { Label = "fnfoo", Detail = "(method) i2.fnfoo(b: number): string", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "this is fnfoo" } } }, new LSP.CompletionItem { Label = "nc_fnfoo", Detail = "(method) i2.nc_fnfoo(b: number): string" }]) } });
                f.VerifyQuickInfoAt(t, "9", "var i2_i_foo: (b: number) => string", "");
                f.VerifyQuickInfoAt(t, "10", "(property) i2.foo: (b: number) => string", "this is foo");
                f.VerifyQuickInfoAt(t, "11", "var i2_i_foo_r: string", "");
                f.GoToMarker(t, "12");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "", ParameterDocComment = "param help" });
                f.VerifyQuickInfoAt(t, "12q", "(property) i2.foo: (b: number) => string", "this is foo");
                f.VerifyQuickInfoAt(t, "13", "var i2_i_i2_si: number", "");
                f.VerifyQuickInfoAt(t, "13q", "var i2_i: i2", "");
                f.VerifyQuickInfoAt(t, "14", "var i2_i_i2_ii: number", "");
                f.VerifyQuickInfoAt(t, "14q", "var i2_i: i2", "");
                f.VerifyQuickInfoAt(t, "15", "var i2_i_n: any", "");
                f.GoToMarker(t, "16");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "new method", ParameterDocComment = "param" });
                f.VerifyQuickInfoAt(t, "16q", "var i2_i: i2\nnew (i: i1) => any", "new method");
                f.VerifyQuickInfoAt(t, "17", "var i2_i_nc_x: number", "");
                f.VerifyQuickInfoAt(t, "18", "(property) i2.nc_x: number", "");
                f.VerifyQuickInfoAt(t, "19", "var i2_i_nc_foo: (b: number) => string", "");
                f.VerifyQuickInfoAt(t, "20", "(property) i2.nc_foo: (b: number) => string", "");
                f.VerifyQuickInfoAt(t, "21", "var i2_i_nc_foo_r: string", "");
                f.GoToMarker(t, "22");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "" });
                f.VerifyQuickInfoAt(t, "22q", "(property) i2.nc_foo: (b: number) => string", "");
                f.VerifyQuickInfoAt(t, "23", "var i2_i_r: number", "");
                f.GoToMarker(t, "24");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "this is call signature", ParameterDocComment = "paramhelp a" });
                f.VerifyQuickInfoAt(t, "24q", "var i2_i: i2\n(a: number, b: number) => number", "this is call signature");
                f.GoToMarker(t, "25");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "this is call signature", ParameterDocComment = "paramhelp b" });
                f.VerifyQuickInfoAt(t, "26", "var i2_i_fnfoo: (b: number) => string", "");
                f.VerifyQuickInfoAt(t, "27", "(method) i2.fnfoo(b: number): string", "this is fnfoo");
                f.VerifyQuickInfoAt(t, "28", "var i2_i_fnfoo_r: string", "");
                f.GoToMarker(t, "29");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "this is fnfoo", ParameterDocComment = "param help" });
                f.VerifyQuickInfoAt(t, "29q", "(method) i2.fnfoo(b: number): string", "this is fnfoo");
                f.VerifyQuickInfoAt(t, "30", "var i2_i_nc_fnfoo: (b: number) => string", "");
                f.VerifyQuickInfoAt(t, "31", "(method) i2.nc_fnfoo(b: number): string", "");
                f.VerifyQuickInfoAt(t, "32", "var i2_i_nc_fnfoo_r: string", "");
                f.GoToMarker(t, "33");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "" });
                f.VerifyQuickInfoAt(t, "33q", "(method) i2.nc_fnfoo(b: number): string", "");
                f.VerifyCompletions(t, "34", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "i1_i", Detail = "var i1_i: i1" }, new LSP.CompletionItem { Label = "nc_i1_i", Detail = "var nc_i1_i: nc_i1", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "" } } }, new LSP.CompletionItem { Label = "i2_i", Detail = "var i2_i: i2" }, new LSP.CompletionItem { Label = "i2_i_x", Detail = "var i2_i_x: number" }, new LSP.CompletionItem { Label = "i2_i_foo", Detail = "var i2_i_foo: (b: number) => string" }, new LSP.CompletionItem { Label = "i2_i_foo_r", Detail = "var i2_i_foo_r: string" }, new LSP.CompletionItem { Label = "i2_i_i2_si", Detail = "var i2_i_i2_si: number" }, new LSP.CompletionItem { Label = "i2_i_i2_ii", Detail = "var i2_i_i2_ii: number" }, new LSP.CompletionItem { Label = "i2_i_n", Detail = "var i2_i_n: any" }, new LSP.CompletionItem { Label = "i2_i_nc_x", Detail = "var i2_i_nc_x: number" }, new LSP.CompletionItem { Label = "i2_i_nc_foo", Detail = "var i2_i_nc_foo: (b: number) => string" }, new LSP.CompletionItem { Label = "i2_i_nc_foo_r", Detail = "var i2_i_nc_foo_r: string" }, new LSP.CompletionItem { Label = "i2_i_r", Detail = "var i2_i_r: number" }, new LSP.CompletionItem { Label = "i2_i_fnfoo", Detail = "var i2_i_fnfoo: (b: number) => string" }, new LSP.CompletionItem { Label = "i2_i_fnfoo_r", Detail = "var i2_i_fnfoo_r: string" }, new LSP.CompletionItem { Label = "i2_i_nc_fnfoo", Detail = "var i2_i_nc_fnfoo: (b: number) => string" }, new LSP.CompletionItem { Label = "i2_i_nc_fnfoo_r", Detail = "var i2_i_nc_fnfoo_r: string" }], Excludes = ["i1", "nc_i1", "i2"] } });
                f.VerifyCompletions(t, "34i", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "i1", Detail = "interface i1", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "this is interface 1" } } }, new LSP.CompletionItem { Label = "nc_i1", Detail = "interface nc_i1" }, new LSP.CompletionItem { Label = "i2", Detail = "interface i2", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "this is interface 2 with members" } } }] } });
                f.VerifyCompletions(t, "36", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "a", Detail = "(parameter) a: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "i3_i a" } } }] } });
                f.VerifyQuickInfoAt(t, "40q", "var i3_i: i3", "");
                f.VerifyCompletions(t, "40", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "i3_i", Detail = "var i3_i: i3" }], Excludes = ["i3"] } });
                f.GoToMarker(t, "41");
                f.VerifyQuickInfoIs(t, "(method) i3.f(a: number): string", "Function i3 f");
                f.VerifyCompletions(t, "41", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "f", Detail = "(method) i3.f(a: number): string", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Function i3 f" } } }, new LSP.CompletionItem { Label = "l", Detail = "(property) i3.l: (b: number) => string", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "i3 l" } } }, new LSP.CompletionItem { Label = "nc_f", Detail = "(method) i3.nc_f(a: number): string" }, new LSP.CompletionItem { Label = "nc_l", Detail = "(property) i3.nc_l: (b: number) => string" }, new LSP.CompletionItem { Label = "nc_x", Detail = "(property) i3.nc_x: number" }, new LSP.CompletionItem { Label = "x", Detail = "(property) i3.x: number", Documentation = new LSP.StringOrMarkupContent { MarkupContent = new LSP.MarkupContent { Kind = LSP.MarkupKind.Markdown, Value = "Comment i3 x" } } }] } });
                f.GoToMarker(t, "42");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "Function i3 f", ParameterDocComment = "number parameter" });
                f.GoToMarker(t, "43");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "", ParameterDocComment = "comment i3 l b" });
                f.VerifyQuickInfoAt(t, "43q", "(property) i3.l: (b: number) => string", "i3 l");
                f.GoToMarker(t, "44");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "" });
                f.VerifyQuickInfoAt(t, "44q", "(method) i3.nc_f(a: number): string", "");
                f.GoToMarker(t, "45");
                f.VerifySignatureHelp(t, new F.VerifySignatureHelpOptions { DocComment = "" });
                f.VerifyQuickInfoAt(t, "45q", "(property) i3.nc_l: (b: number) => string", "");
            }
            finally
            {
                done();
            }
        });
    }

}
