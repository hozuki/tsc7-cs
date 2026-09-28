// ported from: internal/fourslash/tests/completionListForDerivedType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListForDerivedType1Case
{
    public static void TestCompletionListForDerivedType1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface IFoo {\n    bar(): IFoo;\n}\ninterface IFoo2 extends IFoo {\n    bar2(): IFoo2;\n}\nvar f: IFoo;\nvar f2: IFoo2;\nf./*1*/; // completion here shows bar with return type is any\nf2./*2*/ // here bar has return type any, but bar2 is Foo2"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "bar", Detail = "(method) IFoo.bar(): IFoo" }] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = [new LSP.CompletionItem { Label = "bar", Detail = "(method) IFoo.bar(): IFoo" }, new LSP.CompletionItem { Label = "bar2", Detail = "(method) IFoo2.bar2(): IFoo2" }] } });
            }
            finally
            {
                done();
            }
        });
    }

}
