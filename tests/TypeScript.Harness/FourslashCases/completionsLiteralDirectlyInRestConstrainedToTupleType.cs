// ported from: internal/fourslash/tests/completionsLiteralDirectlyInRestConstrainedToTupleType_test.go
// 本文件由 artifacts/fourslash-gen/gen.mjs 机械生成（不手工编辑）。
// 偏离清单：无（用例体逐条对应 Go 原文；`defer done()` → try/finally，`defer testutil.RecoverAndFail` → 包裹 lambda）。
using F = TypeScript.Harness.Fourslash;
using TSU = TypeScript.TestUtil;
using U = TypeScript.Harness.FourslashCases.FourslashTestUtil;

namespace TypeScript.Harness.FourslashCases;

internal static class CompletionsLiteralDirectlyInRestConstrainedToTupleTypeCase
{
    public static void TestCompletionsLiteralDirectlyInRestConstrainedToTupleType(TSU.IHarnessTest t)
    {
        t.Parallel();
        TSU.TestUtil.RecoverAndFail(t, "Panic on fourslash test", () =>
        {
            var content = FourslashCaseContent.U8("// @strict: true\n\ninterface Func {\n  <Key extends \"a\" | \"b\">(\n    ...args:\n      | [key: Key, options?: any]\n      | [key: Key, defaultValue: string, options?: any]\n  ): string;\n}\n\ndeclare const func: Func;\n\nfunc(\"/*1*/\");"u8);
            var (f, done) = F.Fourslash.NewFourslash(t, null!, content);
            try
            {
                f.VerifyCompletions(t, new F.MarkerInput { MarkerNames = ["1"] }, new F.CompletionsExpectedList { IsIncomplete = false, ItemDefaults = new F.CompletionsExpectedItemDefaults { CommitCharacters = U.DefaultCommitCharacters, EditRange = F.Ignored.Value }, Items = new F.CompletionsExpectedItems { Exact = ["a", "b"] } });
            }
            finally
            {
                done();
            }
        });
    }

}
