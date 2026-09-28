// ported from: internal/fourslash/tests/completionListEnumValues_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionListEnumValuesCase
{
    public static void TestCompletionListEnumValues(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("enum Colors {\n    Red,\n    Green\n}\n\nColors./*enumVariable*/;\n\nvar x = Colors.Red;\nx./*variableOfEnumType*/;\n\nfunction foo(): Colors { return null; }\nfoo()./*callOfEnumReturnType*/"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, "enumVariable", new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["Green", "Red"] } });
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["variableOfEnumType", "callOfEnumReturnType"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["toExponential", "toFixed", "toLocaleString", "toPrecision", "toString", "valueOf"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
