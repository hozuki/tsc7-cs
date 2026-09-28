// ported from: internal/fourslash/tests/completionListCladule_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListCladuleCase
{
    public static void TestCompletionListCladule(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("class Foo {\n    doStuff(): number { return 0; }\n    static staticMethod() {}\n}\nnamespace Foo {\n    export var x: number;\n}\nFoo/*c1*/; // should get \"x\", \"prototype\"\nvar s: Foo/*c2*/; // no types, in Foo, so shouldnt have anything\nvar f = new Foo();\nf/*c3*/;"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.GoToMarker(t, "c1");
                f.Insert(t, ".");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "x", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "staticMethod", SortText = LS.SortTextLocalDeclarationPriority }] } });
                f.GoToMarker(t, "c2");
                f.Insert(t, ".");
                f.VerifyCompletions(t, null!, null!);
                f.GoToMarker(t, "c3");
                f.Insert(t, ".");
                f.VerifyCompletions(t, null!, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["doStuff"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
