// ported from: internal/fourslash/tests/completionListInExtendsClause_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInExtendsClauseCase
{
    public static void TestCompletionListInExtendsClause(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @lib: es5\ninterface IFoo {\n    method();\n}\n\nclass Foo {\n    property: number;\n    method() { }\n    static staticMethod() { }\n}\nclass test1 extends Foo./*1*/ {}\nclass test2 implements IFoo./*2*/ {}\ninterface test3 extends IFoo./*3*/ {}\ninterface test4 implements Foo./*4*/ {}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = U.CompletionFunctionMembersPlus([new LSP.CompletionItem { Label = "staticMethod", SortText = LS.SortTextLocalDeclarationPriority }, new LSP.CompletionItem { Label = "prototype", SortText = LS.SortTextLocationPriority }]) } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["2", "3", "4"] }, null!);
            }
            finally
            {
                done();
            }
        });
    }

}
