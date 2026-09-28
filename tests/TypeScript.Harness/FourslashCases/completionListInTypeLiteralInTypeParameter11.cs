// ported from: internal/fourslash/tests/completionListInTypeLiteralInTypeParameter11_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInTypeLiteralInTypeParameter11Case
{
    public static void TestCompletionListInTypeLiteralInTypeParameter11(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    one: string;\n    two: number;\n}\ninterface Bar {\n    three: boolean;\n    four: symbol;\n}\n\nclass A<T extends Foo> {}\nnew A<{/*0*/}>();\n\nclass B<T extends Foo, U extends Bar> {}\nnew B<{/*1*/}, {/*2*/}>();\n\ndeclare const C: {\n   new <T extends Foo>(): unknown\n   new <T extends Bar>(): unknown\n}\nnew C<{/*3*/}>()\n\nnew (class <T extends Foo> {})<{/*4*/}>();"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["one", "two"] } });
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["one", "two"] } });
                f.VerifyCompletions(t, "2", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["three", "four"] } });
                f.VerifyCompletions(t, "3", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["one", "two", "three", "four"] } });
                f.VerifyCompletions(t, "4", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["one", "two"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
