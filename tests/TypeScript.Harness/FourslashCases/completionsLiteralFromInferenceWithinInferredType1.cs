// ported from: internal/fourslash/tests/completionsLiteralFromInferenceWithinInferredType1_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsLiteralFromInferenceWithinInferredType1Case
{
    public static void TestCompletionsLiteralFromInferenceWithinInferredType1(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @stableTypeOrdering: true\n// @Filename: /a.tsx\ndeclare function test<T>(a: {\n  [K in keyof T]: {\n    b?: keyof T;\n  };\n}): void;\n\ntest({\n  foo: {},\n  bar: {\n    b: \"/*ts*/\",\n  },\n});\n\ntest({\n  foo: {},\n  bar: {\n    b: /*ts2*/,\n  },\n});"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["bar", "foo"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["ts2"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Includes = ["\"bar\"", "\"foo\""] } });
            }
            finally
            {
                done();
            }
        });
    }

}
