// ported from: internal/fourslash/tests/completionListInTypeLiteralInTypeParameter1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListInTypeLiteralInTypeParameter1Case
{
    public static void TestCompletionListInTypeLiteralInTypeParameter1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("interface Foo {\n    one: string;\n    two: number;\n    333: symbol;\n    '4four': boolean;\n    '5 five': object;\n    number: string;\n    Object: number;\n}\n\ninterface Bar<T extends Foo> {\n    foo: T;\n}\n\nvar foobar: Bar<{/**/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = [], EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Unsorted = ["one", "two", "\"333\"", "\"4four\"", "\"5 five\"", "number", "Object"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
