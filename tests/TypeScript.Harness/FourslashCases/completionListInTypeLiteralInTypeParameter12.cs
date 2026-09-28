// ported from: internal/fourslash/tests/completionListInTypeLiteralInTypeParameter12_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInTypeLiteralInTypeParameter12Case
{
    public static void TestCompletionListInTypeLiteralInTypeParameter12(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    kind: 'foo';\n    one: string;\n}\ninterface Bar {\n    kind: 'bar';\n    two: number;\n}\n\ndeclare function a<T extends Foo>(): void\ndeclare function a<T extends Bar>(): void\na<{ kind: 'bar', /*0*/ }>();\n\ndeclare function b<T extends Foo>(kind: 'foo'): void\ndeclare function b<T extends Bar>(kind: 'bar'): void\nb<{/*1*/}>('bar');"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "0", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["one", "two"] } });
                f.VerifyCompletions(t, "1", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["kind", "one", "two"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
