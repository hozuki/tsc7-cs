// ported from: internal/fourslash/tests/multiModuleClodule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class MultiModuleCloduleCase
{
    public static void TestMultiModuleClodule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\nclass C {\n    constructor(x: number) { }\n    foo() { }\n    bar() { }\n    static boo() { }\n}\n\nnamespace C {\n    export var x = 1;\n    var y = 2;\n}\nnamespace C {\n    export function foo() { }\n    function baz() { return ''; }\n}\n\nvar c = new C/*1*/(C./*2*/x);\nc./*3*/foo = C./*4*/foo;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["C"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "4"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "boo", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "foo", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "x", SortText = LS.SortTextLocationPriority }]) } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["bar", "foo"] } });
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
