// ported from: internal/fourslash/tests/cloduleTypeOf1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using LS = TypeScript.Ls.Ls;
using LSP = TypeScript.LsProto;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CloduleTypeOf1Case
{
    public static void TestCloduleTypeOf1(TSU.IHarnessTest t)
    {
        t.Skip("Known failing fourslash test");
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: false\nclass C<T> {\n    static foo(x: number) { }\n    x: T;\n}\n\nnamespace C {\n    export function f(x: typeof C) {\n        x./*1*/\n        var /*3*/r = new /*2*/x<number>();\n        var /*5*/r2 = r./*4*/\n        return typeof r;\n    }\n}"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = [new LSP.CompletionItem { Label = "f", SortText = LS.SortTextLocationPriority }, new LSP.CompletionItem { Label = "foo", SortText = LS.SortTextLocalDeclarationPriority }] } });
                f.Insert(t, "foo(1);");
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["x"] } });
                f.VerifyQuickInfoAt(t, "3", "(local var) r: C<number>", "");
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["x"] } });
                f.Insert(t, "x;");
                f.VerifyQuickInfoAt(t, "5", "(local var) r2: number", "");
                f.VerifyNoErrors(t);
            }
            finally
            {
                done();
            }
        });
    }

}
